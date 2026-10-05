// Raccoglie il materiale della misura V2: per ogni forma, le IMMAGINI e lo
// STATO TESTUALE, ognuno in un file.
//
//   node collect_views.js                 tutte le forme in cases/views
//   node collect_views.js cases/views/2_open_shell.json
//
// PERCHE' UN RACCOGLITORE E NON UNA PROVA. La voce V2 chiede una misura che
// questo programma NON puo' fare: dare a un modello **solo** le immagini, poi
// **solo** lo stato, poi tutti e due, e contare quante volte ci prende. Quello
// lo fa chi legge. Qui si prepara il materiale, identico per tutte e tre le
// condizioni, in modo che la differenza fra le tre sia solo cio' che si vuole
// misurare.
//
// COSA SCRIVE, in `Bench/viste/`:
//
//   <forma>-isometrica.png
//   <forma>-frontale.png  <forma>-superiore.png  <forma>-destra.png
//   <forma>-stato.json          lo stato SENZA immagini, come lo riceve oggi
//   <forma>-verita.txt          cos'e' davvero, preso da `_truth` nel piano
//
// La verita' sta nel file del piano e non qui: chi giudica non deve poterla
// leggere insieme al materiale, e chi scrive il piano e' l'unico che la sa.
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");
const OUTPUT = path.join(__dirname, "viste");

// DUE CHIAMATE, NON UNA: il ponte ne da' al massimo tre per volta, ed e' un
// tetto voluto (una vista costa piu' di dieci volte l'intero stato scritto).
// Quattro viste sono due chiamate, e va bene cosi': questo e' il raccoglitore,
// non il ponte.
const FIRST_ROUND = ["isometric"];
const SECOND_ROUND = ["front", "top", "right"];

const data = process.argv.slice(2);
const planes = data.length
  ? data
  : fs.readdirSync(path.join(__dirname, "cases", "views"))
      .filter(f => f.endsWith(".json"))
      .sort()
      .map(f => path.join(__dirname, "cases", "views", f));

if (!planes.length) { console.error("nessun piano da raccogliere"); process.exit(2); }
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

// LO STATO SI ESTRAE DAL TESTO, e il testo non e' JSON puro quando ci sono
// immagini: davanti c'e' la didascalia. Si prende da dove comincia l'oggetto.
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
  const truth = (raw.find(p => p._truth) || {})._truth || "(non dichiarata)";

  // Il piano che si manda non porta i campi che cominciano per `_`: sono note
  // per chi legge il file, e `build` rifiuterebbe argomenti che lo schema
  // non conosce.
  const steps = raw.map(p => ({ operation: p.operation, arguments: p.arguments }));

  console.log("");
  console.log("=== " + itemName + " ===");

  const r = spawnSync(NEW, [], { encoding: "utf8" });
  if (r.status !== 0) { console.log("  NO  parte nuova non creata"); return false; }

  await invoke("inspect_part");
  const done = await invoke("build", { steps: steps });
  if (done.errore) {
    console.log("  NO  il piano non e' passato: " +
                done.testo.replace(/[\r\n]+/g, " ").slice(0, 200));
    return false;
  }
  const volume = (done.dati || {}).volume_after_mm3;
  console.log("  ok  costruito, volume " + volume + " mm3");

  // ── lo stato, senza immagini: e' quello che il modello riceve OGGI ──
  const stateOnly = await invoke("inspect_part");
  const docState = stateOnly.dati || stateFromText(stateOnly.testo);
  fs.writeFileSync(path.join(OUTPUT, itemName + "-stato.json"),
                   JSON.stringify(docState, null, 1));
  const characters = fs.statSync(path.join(OUTPUT, itemName + "-stato.json")).size;
  console.log("  ok  stato: " + characters + " caratteri, " +
              ((docState.faces || []).length) + " facce, " +
              ((docState.feature || []).length) + " lavorazioni");

  // ── le immagini ──
  const one = await invoke("inspect_part", { views: FIRST_ROUND });
  const three = await invoke("inspect_part", { views: SECOND_ROUND });
  const written = saveImages(itemName, FIRST_ROUND, one.immagini)
    .concat(saveImages(itemName, SECOND_ROUND, three.immagini));
  if (!written.length) {
    console.log("  NO  nessuna immagine: " +
                one.testo.replace(/[\r\n]+/g, " ").slice(0, 200));
    return false;
  }
  console.log("  ok  " + written.join(", "));

  fs.writeFileSync(path.join(OUTPUT, itemName + "-verita.txt"),
                   truth + "\nvolume misurato: " + volume + " mm3\n");
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
  console.log(fine + " forme su " + planes.length + " raccolte in " + OUTPUT);
  bridge.stdin.end();
  process.exitCode = fine === planes.length ? 0 : 1;
})();
