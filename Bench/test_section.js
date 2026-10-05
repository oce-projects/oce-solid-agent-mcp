// V4 — does the cutaway show the wall?
//
//   node test_section.js
//
// THE CRITERION OF THE ENTRY, literally: «on a shell, the section image
// shows the wall, and from it the thickness can be read».
//
// The second half — «the thickness can be read» — can be said only by someone who
// LOOKS. This program prepares the two images of the same shell, a whole
// one and a sectioned one, and leaves them on disk so that an isolated model
// can compare them. What it can check by itself it checks:
//
//   * the section comes out, and it is a PNG;
//   * **it is DIFFERENT from the whole view** — which is the real red: a section
//     that does not section would give an identical image, and «the image comes out»
//     would pass all the same;
//   * afterwards, the part on the screen of whoever works is as before: no
//     section left on, no body cut, no feature more.
//
// The last is what the entry calls the risk: «the section plane must be PUT
// AWAY afterwards».
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");
const OUTPUT = path.join(__dirname, "viste");

// The same shell as measurement V2/V3: box 120x80x50, wall 4, open at the
// top. From outside it is a parallelepiped; the wall is seen only inside.
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

  // ── as it was before, to be able to compare it afterwards ──────────────
  const before = await invoke("inspect_part", { views: ["isometric"] });
  if (!before.immagini.length) { check("la vista intera", false, shorter(before.testo)); bridge.stdin.end(); return; }
  const wholeFile = save("v4-guscio-intero", before.immagini[0]);
  const stateBefore = before.dati || {};
  check("la vista intera esce", true, path.basename(wholeFile));

  // ── the cutaway ────────────────────────────────────────────────────────
  const wasCut = await invoke("inspect_part", {
    section: { plane: "right", offset_mm: 0 },
  });
  if (!wasCut.immagini.length) {
    check("la sezione esce", false, shorter(wasCut.testo));
  } else {
    const sectionFile = save("v4-guscio-sezione", wasCut.immagini[0]);
    check("la sezione esce", true, path.basename(sectionFile));

    // THE RED: a section that does not section gives the SAME image, and
    // «la sezione esce» would pass all the same.
    const a = fs.readFileSync(wholeFile);
    const b = fs.readFileSync(sectionFile);
    check("la sezione e' DIVERSA dalla vista intera",
          !a.equals(b),
          a.equals(b) ? "identiche: non ha sezionato niente"
                      : a.length + " byte contro " + b.length);
  }

  // ── and the part of whoever works is as before ─────────────────────────
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
