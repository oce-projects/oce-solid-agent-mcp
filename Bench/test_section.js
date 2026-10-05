// V4 — lo spaccato mostra la parete?
//
//   node test_section.js
//
// IL CRITERIO DELLA VOCE, alla lettera: «su un guscio, l'immagine in sezione
// mostra la parete, e da quella si legge lo spessore».
//
// La seconda meta' — «si legge lo spessore» — la puo' dire solo qualcuno che
// GUARDA. Questo programma prepara le due immagini dello stesso guscio, una
// intera e una in sezione, e le lascia su disco perche' un modello isolato le
// confronti. Quello che puo' controllare da solo lo controlla:
//
//   * la sezione esce, ed e' un PNG;
//   * **e' DIVERSA dalla vista intera** — che e' il rosso vero: una sezione
//     che non seziona darebbe un'immagine identica, e «l'immagine esce»
//     passerebbe lo stesso;
//   * dopo, il pezzo sullo schermo di chi lavora e' come prima: nessuna
//     sezione rimasta accesa, nessun corpo tagliato, nessuna feature in piu'.
//
// L'ultimo e' quello che la voce chiama rischio: «il piano di sezione va MESSO
// VIA dopo».
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");
const OUTPUT = path.join(__dirname, "viste");

// Lo stesso guscio della misura V2/V3: scatola 120x80x50, parete 4, aperta in
// alto. Dall'esterno e' un parallelepipedo; la parete si vede solo dentro.
const SHELL = [
  {
    operation: "extrude_rectangle",
    arguments: {
      plane: "top", length_mm: 120, width_mm: 80,
      thickness_mm: 50, centered: true,
    },
  },
  {
    operation: "shell",
    arguments: {
      faces_ref: [{ step: 1, kind: "top_face" }],
      thickness_mm: 4,
    },
  },
];

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
  const parts = r.content || [];
  const text = parts.filter(c => c.type === "text").map(c => c.text).join("\n");
  const images = parts.filter(c => c.type === "image");
  let data = null;
  const i = text.indexOf("{");
  if (i >= 0) { try { data = JSON.parse(text.slice(i)); } catch (e) {} }
  return { testo: text, dati: data, immagini: images, errore: !!r.isError };
}

let failed = 0;
function check(itemName, ok, detail) {
  if (!ok) failed++;
  console.log((ok ? "  OK   " : "  NO   ") + itemName.padEnd(46) + (detail || ""));
}

function shorter(s) {
  if (!s) return "(niente)";
  const one = s.replace(/[\r\n]+/g, " ");
  return one.length > 120 ? one.slice(0, 120) + "..." : one;
}

function save(itemName, image) {
  const file = path.join(OUTPUT, itemName + ".png");
  fs.writeFileSync(file, Buffer.from(image.data, "base64"));
  return file;
}

(async function () {
  await send("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_sezione", version: "1" },
  });
  warn("notifications/initialized");
  fs.mkdirSync(OUTPUT, { recursive: true });

  console.log("");
  console.log("=== lo spaccato mostra la parete? (V4) ===");
  console.log("");

  const r = spawnSync(NEW, [], { encoding: "utf8" });
  if (r.status !== 0) { check("una parte pulita", false, "NewPart non risponde"); return; }

  await invoke("inspect_part");
  const done = await invoke("build", { steps: SHELL });
  if (done.errore) { check("il guscio", false, shorter(done.testo)); bridge.stdin.end(); return; }
  check("il guscio si costruisce", true,
        "volume " + (done.dati || {}).volume_after_mm3);

  // ── com'era prima, per poterlo confrontare dopo ────────────────────────
  const before = await invoke("inspect_part", { views: ["isometric"] });
  if (!before.immagini.length) { check("la vista intera", false, shorter(before.testo)); bridge.stdin.end(); return; }
  const wholeFile = save("v4-guscio-intero", before.immagini[0]);
  const stateBefore = before.dati || {};
  check("la vista intera esce", true, path.basename(wholeFile));

  // ── lo spaccato ────────────────────────────────────────────────────────
  const wasCut = await invoke("inspect_part", {
    section: { plane: "right", offset_mm: 0 },
  });
  if (!wasCut.immagini.length) {
    check("la sezione esce", false, shorter(wasCut.testo));
  } else {
    const sectionFile = save("v4-guscio-sezione", wasCut.immagini[0]);
    check("la sezione esce", true, path.basename(sectionFile));

    // IL ROSSO: una sezione che non seziona da' la STESSA immagine, e
    // «la sezione esce» passerebbe lo stesso.
    const a = fs.readFileSync(wholeFile);
    const b = fs.readFileSync(sectionFile);
    check("la sezione e' DIVERSA dalla vista intera",
          !a.equals(b),
          a.equals(b) ? "identiche: non ha sezionato niente"
                      : a.length + " byte contro " + b.length);
  }

  // ── e il pezzo di chi lavora e' come prima ─────────────────────────────
  const after = await invoke("inspect_part");
  const s = after.dati || {};
  check("il pezzo non e' cambiato",
        s.volume_mm3 === stateBefore.volume_mm3 &&
        s.bodies_total === stateBefore.bodies_total &&
        (s.feature || []).length === (stateBefore.feature || []).length,
        "volume " + s.volume_mm3 + ", corpi " + s.bodies_total +
        ", lavorazioni " + (s.feature || []).length);

  console.log("");
  console.log("Le due immagini sono in " + OUTPUT + ":");
  console.log("  v4-guscio-intero.png    v4-guscio-sezione.png");
  console.log("Guardarle e' l'altra meta' del criterio: dalla sezione si deve " +
              "LEGGERE lo spessore della parete.");
  console.log("");
  console.log(failed === 0 ? "Tutto torna." : failed + " prove fallite.");
  bridge.stdin.end();
  process.exitCode = failed === 0 ? 0 : 1;
})();
