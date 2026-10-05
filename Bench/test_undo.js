// Prova lo strumento `undo` (voce R1) parlando al ponte come un client MCP.
//
//   node test_undo.js
//
// PERCHE' QUI E NON NEL BANCO. `Oce.Solid.Agent.Bench` prova la LIBRERIA per
// riflessione; `undo` non sta nella libreria — e' uno strumento del ponte,
// e la parte che conta (il ricordo dell'ultimo piano, i rifiuti) vive in
// `McpTools`. Provarlo dalla libreria vorrebbe dire provare un'altra cosa.
//
// ── COSA AFFERMA, E I TRE ROSSI ──────────────────────────────────────────
//
// Il verde e' uno: un piano di due passi viene ritirato tutto intero e il pezzo
// torna a volume zero. I rossi sono tre, e sono la ragione per cui questo file
// esiste — «undo» che funziona non e' la parte difficile, «undo» che si
// RIFIUTA lo e':
//
//   R1a  senza nessun piano eseguito          -> deve rifiutare
//   R1b  chiamato due volte di fila           -> il secondo deve rifiutare,
//        perche' toglierebbe qualcosa che questo ponte non ha fatto
//   R1c  con un altro documento davanti       -> deve rifiutare e dirlo
//
// Senza questi tre, «premi sempre annulla» passerebbe la prova.
//
// Percorsi con la barra avanti: Windows li accetta, e cosi' non c'e' nessun
// livello di escape da sbagliare.
const { spawn, spawnSync } = require("child_process");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");

const PLAN_TWO_STEPS = [
  {
    operation: "extrude_rectangle",
    arguments: {
      plane: "top", length_mm: 200, width_mm: 100,
      thickness_mm: 20, centered: true,
    },
  },
  {
    operation: "cylindrical_cut",
    arguments: {
      face_ref: { step: 1, kind: "top_face" },
      x_mm: 0, y_mm: 0, diameter_mm: 12, through_all: true,
    },
  },
];

const PLAN_ONE_STEP = [
  {
    operation: "extrude_rectangle",
    arguments: {
      plane: "top", length_mm: 80, width_mm: 80,
      thickness_mm: 10, centered: true,
    },
  },
];

// ──────────────────────────────────────────────────────────────────────────
// il cliente MCP, una richiesta per volta

let nextId = 1;
const expectation = new Map();
let leftover = "";

const bridge = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
bridge.stderr.on("data", () => {});   // i messaggi di servizio non sono risposte
bridge.stdout.on("data", d => {
  leftover += d.toString();
  let i;
  while ((i = leftover.indexOf("\n")) >= 0) {
    const line = leftover.slice(0, i).trim();
    leftover = leftover.slice(i + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch (e) { continue; }
    const chi = expectation.get(m.id);
    if (chi) { expectation.delete(m.id); chi(m); }
  }
});

function send(method, parameters) {
  const id = nextId++;
  return new Promise(resolve => {
    expectation.set(id, resolve);
    bridge.stdin.write(JSON.stringify(
      { jsonrpc: "2.0", id: id, method: method, params: parameters }) + "\n");
  });
}

function warn(method) {
  bridge.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: method }) + "\n");
}

// Il testo di una risposta, piu' `isError`. Un'operazione che fallisce non e'
// un errore di protocollo: sta nel contenuto, ed e' li' che va guardata.
async function invoke(tool, args) {
  const m = await send("tools/call",
    { name: tool, arguments: args || {} });
  const r = m.result || {};
  const text = (r.content || []).map(c => c.text).filter(Boolean).join("\n");
  let data = null; try { data = JSON.parse(text); } catch (e) {}
  return { testo: text, dati: data, errore: !!r.isError };
}

// ──────────────────────────────────────────────────────────────────────────

let failed = 0;
function check(itemName, ok, detail) {
  if (!ok) failed++;
  console.log((ok ? "  OK   " : "  NO   ") + itemName.padEnd(56) + (detail || ""));
}

function shorter(s) {
  if (!s) return "(niente)";
  const one = s.replace(/[\r\n]+/g, " ");
  return one.length > 100 ? one.slice(0, 100) + "..." : one;
}

function cleanPart() {
  const r = spawnSync(NEW, [], { encoding: "utf8" });
  return r.status === 0;
}

async function benchRun() {
  await send("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_annulla", version: "1" },
  });
  warn("notifications/initialized");

  // Lo strumento deve ESSERCI, e dichiararsi per quello che fa.
  const list = await send("tools/list", {});
  const tools = (list.result || {}).tools || [];
  const names = tools.map(function (t) { return t.name; });
  check("annulla e' fra gli strumenti", names.indexOf("annulla") >= 0,
        names.join(", "));
  if (names.indexOf("annulla") < 0) return;

  const its = tools.filter(function (t) { return t.name === "undo"; })[0] || {};
  const ann = its.annotations || {};
  check("annulla si dichiara distruttivo e non idempotente",
        ann.destructiveHint === true && ann.idempotentHint === false &&
        ann.readOnlyHint === false,
        JSON.stringify(ann));

  // ── ROSSO 1: niente da ritirare ────────────────────────────────────────
  // Questa e' la PRIMA chiamata della conversazione: il ponte non ha eseguito
  // nessun piano, quindi non sa cosa toglierebbe.
  const empty = await invoke("undo");
  check("ROSSO 1: senza nessun piano, rifiuta",
        empty.errore && /nessun piano da ritirare/i.test(empty.testo),
        shorter(empty.testo));

  if (!cleanPart()) {
    check("una parte pulita", false, "NewPart non ha risposto");
    return;
  }

  const seen = await invoke("inspect_part");
  check("il pezzo pulito si legge", !seen.errore && seen.dati != null,
        seen.dati ? "volume " + seen.dati.volume_mm3 : shorter(seen.testo));

  // ── IL VERDE: due passi, ritirati insieme ──────────────────────────────
  const done = await invoke("build", { steps: PLAN_TWO_STEPS });
  const volumeAfterPlan = done.dati ? done.dati.volume_after_mm3 : null;
  check("il piano di due passi costruisce", !done.errore && volumeAfterPlan > 0,
        done.errore ? shorter(done.testo) : "volume " + volumeAfterPlan);
  if (done.errore) return;

  const withdrawn = await invoke("undo");
  const d = withdrawn.dati || {};
  check("annulla ritira il piano intero",
        !withdrawn.errore && d.undone === true,
        withdrawn.errore ? shorter(withdrawn.testo) : "undone");
  check("il volume torna a zero",
        d.volume_after_mm3 != null && Math.abs(d.volume_after_mm3) < 0.001,
        "volume " + d.volume_after_mm3 + " (era " + d.volume_before_mm3 + ")");
  check("le lavorazioni tornano a zero",
        d.features_after === 0,
        d.features_before + " -> " + d.features_after);

  // LO STATO NUOVO VIENE CON LA RISPOSTA, e non e' un di piu': dopo un
  // annullamento gli identificativi sono altri, e chi legge deve poterli
  // usare senza una lettura in piu'.
  //
  // NON SI SONDA `volume_mm3`. La prima scrittura di questa riga lo faceva, ed
  // e' uscita rossa su un codice che funzionava: dopo l'annullamento il pezzo
  // e' VUOTO, e su un pezzo vuoto lo stato non scrive nessun volume — lo dice
  // gia' due righe piu' su, «il pezzo pulito si legge: volume undefined».
  // Un'asserzione su un campo che legittimamente non c'e' non prova
  // l'assenza dello stato: prova che chi l'ha scritta non aveva letto lo stato.
  check("la risposta porta lo stato di adesso",
        d.state_after != null &&
        typeof d.state_after.document_title === "string" &&
        ("solid_body" in d.state_after),
        d.state_after ? "titolo «" + d.state_after.document_title + "», solid_body " +
                       d.state_after.solid_body
                     : "assente");

  // ── ROSSO 2: due volte di fila ─────────────────────────────────────────
  // Il primo annullamento ha consumato il ricordo. Il secondo toglierebbe
  // quello che c'era PRIMA del piano, che questo ponte non ha fatto.
  const bis = await invoke("undo");
  check("ROSSO 2: la seconda volta rifiuta",
        bis.errore && /nessun piano da ritirare/i.test(bis.testo),
        shorter(bis.testo));

  // ── ROSSO 3: un altro documento davanti ────────────────────────────────
  const other = await invoke("build", { steps: PLAN_ONE_STEP });
  check("un secondo piano costruisce", !other.errore,
        other.errore ? shorter(other.testo)
                     : "volume " + (other.dati || {}).volume_after_mm3);
  if (other.errore) return;

  if (!cleanPart()) {
    check("ROSSO 3: con un altro documento davanti, rifiuta", false,
          "NewPart non ha risposto");
    return;
  }
  const wrong = await invoke("undo");
  check("ROSSO 3: con un altro documento davanti, rifiuta",
        wrong.errore && /documento e' cambiato/i.test(wrong.testo),
        shorter(wrong.testo));
}

benchRun()
  .then(function () {
    console.log("");
    console.log(failed === 0 ? "Tutto torna." : failed + " prove fallite.");
    bridge.stdin.end();
    process.exitCode = failed === 0 ? 0 : 1;
  })
  .catch(function (e) {
    console.error("il giro si e' rotto: " + (e && e.message));
    bridge.stdin.end();
    process.exitCode = 2;
  });
