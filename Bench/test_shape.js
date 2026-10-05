// V3 — is the sentence enough to REDRAW the part?
//
//   node test_shape.js
//
// WHY THE CRITERION IS THE VOLUME AND NOT A JUDGMENT. Entry V3 asks that
// from the sentence alone a model know how to «redraw the part with the right
// dimensions». «It resembles» is not a measurement: either the redrawn part has the volume
// of the original, or it does not.
//
// The volume is not everything — two different parts can have it equal — but
// getting a dimension wrong almost always changes it, and the difference says **by
// how much** one is wrong instead of saying «no». Together with the bounding box, which is
// checked too, a false positive becomes improbable.
//
// HOW THE WHOLE MEASUREMENT IS REDONE:
//   1. `node collect_views.js`   builds the five real parts and writes
//                                 the state, which contains the field `shape`
//   2. the five sentences are given to an ISOLATED model, which writes the plans in
//      `cases/forma/1..5.json` without having seen anything else
//   3. `node test_shape.js`      executes the plans and compares
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");
const PLANS = path.join(__dirname, "cases", "forma");

// THE REAL PARTS, taken from `Bench/viste/*-stato.json`, which is the file that the
// model has NOT seen. If it is missing, the measurement cannot be made: an
// expected value is not invented.
const VIEWS = path.join(__dirname, "viste");
const SHAPES = [
  "1_drilled_plate", "2_open_shell", "3_l_bracket",
  "4_drilled_flange", "5_stepped_shaft",
];

// How much the volume can be off and still count: 1%.
// Tighter would reject a right part drawn with one extra fillet;
// wider would let through a thickness wrong by one millimeter in
// ten.
const TOLERANCE = 0.01;

// ──────────────────────────────────────────────────────────────────────────

let nextId = 1;
const expectation = new Map();
let leftover = "";

const bridge = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
bridge.stderr.on("data", () => {});
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

async function invoke(tool, args) {
  const m = await send("tools/call", { name: tool, arguments: args || {} });
  const r = m.result || {};
  const text = (r.content || []).filter(c => c.type === "text")
                                 .map(c => c.text).join("\n");
  let data = null;
  const i = text.indexOf("{");
  if (i >= 0) { try { data = JSON.parse(text.slice(i)); } catch (e) {} }
  return { testo: text, dati: data, errore: !!r.isError };
}

// ──────────────────────────────────────────────────────────────────────────

let failed = 0;
function check(itemName, ok, detail) {
  if (!ok) failed++;
  console.log((ok ? "  OK   " : "  NO   ") + itemName.padEnd(26) + (detail || ""));
}

function shorter(s) {
  if (!s) return "(niente)";
  const one = s.replace(/[\r\n]+/g, " ");
  return one.length > 130 ? one.slice(0, 130) + "..." : one;
}

function neighbors(a, b, tolerance) {
  if (a == null || b == null) return false;
  const big = Math.max(Math.abs(a), Math.abs(b), 1);
  return Math.abs(a - b) / big <= tolerance;
}

// The bounding box has no guaranteed order — it depends on how the part was
// built — so it is compared SORTED: 200x15x120 and 120x200x15 are the
// same parallelepiped seen from another side, and the model could not
// know which orientation whoever made the original had chosen.
function sameBoundingBox(a, b) {
  if (!a || !b || a.length !== 3 || b.length !== 3) return false;
  const x = a.slice().sort((p, q) => p - q);
  const y = b.slice().sort((p, q) => p - q);
  return x.every((v, i) => neighbors(v, y[i], TOLERANCE));
}

async function testOne(number, itemName) {
  const planFile = path.join(PLANS, number + ".json");
  if (!fs.existsSync(planFile)) {
    check("pezzo " + number, false, "manca il piano " + planFile);
    return;
  }
  const trueFile = path.join(VIEWS, itemName + "-stato.json");
  if (!fs.existsSync(trueFile)) {
    check("pezzo " + number, false,
          "manca lo stato vero: lancia prima `node collect_views.js`");
    return;
  }
  const real = JSON.parse(fs.readFileSync(trueFile, "utf8"));
  let steps;
  try { steps = JSON.parse(fs.readFileSync(planFile, "utf8")); }
  catch (e) { check("pezzo " + number, false, "il piano non e' JSON: " + e.message); return; }
  if (!Array.isArray(steps)) { check("pezzo " + number, false, "il piano non e' un elenco"); return; }

  const r = spawnSync(NEW, [], { encoding: "utf8" });
  if (r.status !== 0) { check("pezzo " + number, false, "parte nuova non creata"); return; }

  await invoke("inspect_part");
  const done = await invoke("build", { steps: steps });
  if (done.errore) {
    check("pezzo " + number, false, "il piano non passa: " + shorter(done.testo));
    return;
  }

  const rebuilt = (done.dati || {}).state_after || {};
  const rebuiltVolume = rebuilt.volume_mm3;
  const trueVolume = real.volume_mm3;
  const okVolume = neighbors(rebuiltVolume, trueVolume, TOLERANCE);
  const okBoundingBox = sameBoundingBox(rebuilt.bounding_box_mm, real.bounding_box_mm);

  const deviation = (trueVolume && rebuiltVolume)
    ? ((rebuiltVolume - trueVolume) / trueVolume * 100).toFixed(1) + "%"
    : "?";

  check("pezzo " + number + " " + itemName.slice(2), okVolume && okBoundingBox,
        "volume " + (rebuiltVolume == null ? "?" : Math.round(rebuiltVolume * 10) / 10) +
        " contro " + trueVolume + " (" + deviation + ")" +
        (okBoundingBox ? "" : ", INGOMBRO diverso: " + JSON.stringify(rebuilt.bounding_box_mm) +
                            " contro " + JSON.stringify(real.bounding_box_mm)));
}

(async function () {
  await send("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_forma", version: "1" },
  });
  warn("notifications/initialized");

  console.log("");
  console.log("=== la frase basta a ridisegnare il pezzo? (V3) ===");
  console.log("");

  for (let i = 0; i < SHAPES.length; i++) {
    try { await testOne(String(i + 1), SHAPES[i]); }
    catch (e) { check("pezzo " + (i + 1), false, "si e' rotto: " + (e && e.message)); }
  }

  console.log("");
  console.log(failed === 0
    ? "Tutti e cinque ridisegnati dalla sola frase."
    : failed + " pezzi su " + SHAPES.length + " NON si ridisegnano dalla sola frase.");
  console.log("Il numero va nel README accanto a quello di V2: sono due modi di " +
              "dire la stessa cosa, e costano ordini di grandezza diversi.");
  bridge.stdin.end();
  process.exitCode = 0;   // it is a MEASUREMENT, not a check: it does not make a run fail
})();
