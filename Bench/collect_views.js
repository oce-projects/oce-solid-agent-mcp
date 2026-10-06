// Collects the material of measurement V2: for each shape, the IMAGES and the
// TEXTUAL STATE, each in a file.
//
//   node collect_views.js                 all the shapes in cases/views
//   node collect_views.js cases/views/2_open_shell.json
//
// WHY A COLLECTOR AND NOT A CHECK. Entry V2 asks for a measurement that
// this program CANNOT do: give a model **only** the images, then
// **only** the state, then both, and count how many times it gets it right. That
// is done by whoever reads. Here the material is prepared, identical for all three
// conditions, so that the difference among the three is only what one wants to
// measure.
//
// WHAT IT WRITES, in `Bench/viste/`:
//
//   <forma>-isometrica.png
//   <forma>-frontale.png  <forma>-superiore.png  <forma>-destra.png
//   <forma>-stato.json          the state WITHOUT images, as it is received today
//   <forma>-verita.txt          what it really is, taken from `_truth` in the plan
//
// The truth is in the plan's file and not here: whoever judges must not be able to
// read it together with the material, and whoever writes the plan is the only one who knows it.
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");
const OUTPUT = path.join(__dirname, "viste");

// TWO CALLS, NOT ONE: the bridge gives at most three at a time, and it is a
// deliberate ceiling (a view costs more than ten times the whole state written out).
// Four views are two calls, and that is fine: this is the collector,
// not the bridge.
const FIRST_ROUND = ["isometric"];
const SECOND_ROUND = ["front", "top", "right"];

const data = process.argv.slice(2);
const planes = data.length
  ? data
  : fs.readdirSync(path.join(__dirname, "cases", "views"))
      .filter(f => f.endsWith(".json"))
      .sort()
      .map(f => path.join(__dirname, "cases", "views", f));

if (!planes.length) { console.error("no plan to collect"); process.exit(2); }
fs.mkdirSync(OUTPUT, { recursive: true });

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
  let data = null; try { data = JSON.parse(text); } catch (e) {}
  return { testo: text, dati: data, immagini: images, errore: !!r.isError };
}

// ──────────────────────────────────────────────────────────────────────────

function nameOf(filePath) {
  return path.basename(filePath, ".json");
}

// THE STATE IS EXTRACTED FROM THE TEXT, and the text is not pure JSON when there are
// images: the caption comes first. It is taken from where the object begins.
function stateFromText(text) {
  const i = text.indexOf("{");
  if (i < 0) return null;
  try { return JSON.parse(text.slice(i)); } catch (e) { return null; }
}

function saveImages(itemName, whichOnes, images) {
  const written = [];
  for (let k = 0; k < images.length && k < whichOnes.length; k++) {
    const file = path.join(OUTPUT, itemName + "-" + whichOnes[k] + ".png");
    fs.writeFileSync(file, Buffer.from(images[k].data, "base64"));
    written.push(path.basename(file) + " (" +
                 Math.round(fs.statSync(file).size / 1024) + " kB)");
  }
  return written;
}

async function collect(planPath) {
  const itemName = nameOf(planPath);
  const raw = JSON.parse(fs.readFileSync(planPath, "utf8"));
  const truth = (raw.find(p => p._truth) || {})._truth || "(not declared)";

  // The plan that is sent does not carry the fields that start with `_`: they are notes
  // for whoever reads the file, and `build` would refuse arguments that the schema
  // does not know.
  const steps = raw.map(p => ({ operation: p.operation, arguments: p.arguments }));

  console.log("");
  console.log("=== " + itemName + " ===");

  const r = spawnSync(NEW, [], { encoding: "utf8" });
  if (r.status !== 0) { console.log("  NO  new part not created"); return false; }

  await invoke("inspect_part");
  const done = await invoke("build", { steps: steps });
  if (done.errore) {
    console.log("  NO  the plan did not pass: " +
                done.testo.replace(/[\r\n]+/g, " ").slice(0, 200));
    return false;
  }
  const volume = (done.dati || {}).volume_after_mm3;
  console.log("  ok  built, volume " + volume + " mm3");

  // ── the state, without images: it is what the model receives TODAY ──
  const stateOnly = await invoke("inspect_part");
  const docState = stateOnly.dati || stateFromText(stateOnly.testo);
  fs.writeFileSync(path.join(OUTPUT, itemName + "-stato.json"),
                   JSON.stringify(docState, null, 1));
  const characters = fs.statSync(path.join(OUTPUT, itemName + "-stato.json")).size;
  console.log("  ok  state: " + characters + " characters, " +
              ((docState.faces || []).length) + " faces, " +
              ((docState.feature || []).length) + " features");

  // ── the images ──
  const one = await invoke("inspect_part", { views: FIRST_ROUND });
  const three = await invoke("inspect_part", { views: SECOND_ROUND });
  const written = saveImages(itemName, FIRST_ROUND, one.immagini)
    .concat(saveImages(itemName, SECOND_ROUND, three.immagini));
  if (!written.length) {
    console.log("  NO  no image: " +
                one.testo.replace(/[\r\n]+/g, " ").slice(0, 200));
    return false;
  }
  console.log("  ok  " + written.join(", "));

  fs.writeFileSync(path.join(OUTPUT, itemName + "-verita.txt"),
                   truth + "\nmeasured volume: " + volume + " mm3\n");
  return true;
}

(async function () {
  await send("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "raccogli_viste", version: "1" },
  });
  warn("notifications/initialized");

  let fine = 0;
  for (const p of planes) {
    try { if (await collect(p)) fine++; }
    catch (e) { console.log("  NO  " + nameOf(p) + ": " + (e && e.message)); }
  }

  console.log("");
  console.log(fine + " shapes out of " + planes.length + " collected in " + OUTPUT);
  bridge.stdin.end();
  process.exitCode = fine === planes.length ? 0 : 1;
})();
