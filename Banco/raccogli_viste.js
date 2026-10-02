// Raccoglie il materiale della misura V2: per ogni forma, le IMMAGINI e lo
// STATO TESTUALE, ognuno in un file.
//
//   node raccogli_viste.js                 tutte le forme in cases/views
//   node raccogli_viste.js cases/views/2_open_shell.json
//
// PERCHE' UN RACCOGLITORE E NON UNA PROVA. La voce V2 chiede una misura che
// questo programma NON puo' fare: dare a un modello **solo** le immagini, poi
// **solo** lo stato, poi tutti e due, e contare quante volte ci prende. Quello
// lo fa chi legge. Qui si prepara il materiale, identico per tutte e tre le
// condizioni, in modo che la differenza fra le tre sia solo cio' che si vuole
// misurare.
//
// COSA SCRIVE, in `Banco/viste/`:
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

const RADICE = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(RADICE, "bin", "oce-solid-agent-mcp.exe");
const NUOVA = path.join(RADICE, "Controlli", "NuovaParte", "bin", "NuovaParte.exe");
const USCITA = path.join(__dirname, "viste");

// DUE CHIAMATE, NON UNA: il ponte ne da' al massimo tre per volta, ed e' un
// tetto voluto (una vista costa piu' di dieci volte l'intero stato scritto).
// Quattro viste sono due chiamate, e va bene cosi': questo e' il raccoglitore,
// non il ponte.
const PRIMA_MANO = ["isometric"];
const SECONDA_MANO = ["front", "top", "right"];

const dati = process.argv.slice(2);
const piani = dati.length
  ? dati
  : fs.readdirSync(path.join(__dirname, "cases", "views"))
      .filter(f => f.endsWith(".json"))
      .sort()
      .map(f => path.join(__dirname, "cases", "views", f));

if (!piani.length) { console.error("nessun piano da raccogliere"); process.exit(2); }
fs.mkdirSync(USCITA, { recursive: true });

// ──────────────────────────────────────────────────────────────────────────

let prossimoId = 1;
const attesa = new Map();
let avanzo = "";

const ponte = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
ponte.stderr.on("data", () => {});
ponte.stdout.on("data", d => {
  avanzo += d.toString();
  let i;
  while ((i = avanzo.indexOf("\n")) >= 0) {
    const riga = avanzo.slice(0, i).trim();
    avanzo = avanzo.slice(i + 1);
    if (!riga) continue;
    let m; try { m = JSON.parse(riga); } catch (e) { continue; }
    const chi = attesa.get(m.id);
    if (chi) { attesa.delete(m.id); chi(m); }
  }
});

function manda(metodo, parametri) {
  const id = prossimoId++;
  return new Promise(risolvi => {
    attesa.set(id, risolvi);
    ponte.stdin.write(JSON.stringify(
      { jsonrpc: "2.0", id: id, method: metodo, params: parametri }) + "\n");
  });
}

function avvisa(metodo) {
  ponte.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: metodo }) + "\n");
}

async function chiama(strumento, argomenti) {
  const m = await manda("tools/call", { name: strumento, arguments: argomenti || {} });
  const r = m.result || {};
  const pezzi = r.content || [];
  const testo = pezzi.filter(c => c.type === "text").map(c => c.text).join("\n");
  const immagini = pezzi.filter(c => c.type === "image");
  let dati = null; try { dati = JSON.parse(testo); } catch (e) {}
  return { testo: testo, dati: dati, immagini: immagini, errore: !!r.isError };
}

// ──────────────────────────────────────────────────────────────────────────

function nomeDi(percorso) {
  return path.basename(percorso, ".json");
}

// LO STATO SI ESTRAE DAL TESTO, e il testo non e' JSON puro quando ci sono
// immagini: davanti c'e' la didascalia. Si prende da dove comincia l'oggetto.
function statoDalTesto(testo) {
  const i = testo.indexOf("{");
  if (i < 0) return null;
  try { return JSON.parse(testo.slice(i)); } catch (e) { return null; }
}

function salvaImmagini(nome, quali, immagini) {
  const scritti = [];
  for (let k = 0; k < immagini.length && k < quali.length; k++) {
    const file = path.join(USCITA, nome + "-" + quali[k] + ".png");
    fs.writeFileSync(file, Buffer.from(immagini[k].data, "base64"));
    scritti.push(path.basename(file) + " (" +
                 Math.round(fs.statSync(file).size / 1024) + " kB)");
  }
  return scritti;
}

async function raccogli(percorsoPiano) {
  const nome = nomeDi(percorsoPiano);
  const grezzo = JSON.parse(fs.readFileSync(percorsoPiano, "utf8"));
  const verita = (grezzo.find(p => p._truth) || {})._truth || "(non dichiarata)";

  // Il piano che si manda non porta i campi che cominciano per `_`: sono note
  // per chi legge il file, e `build` rifiuterebbe argomenti che lo schema
  // non conosce.
  const passi = grezzo.map(p => ({ operation: p.operation, arguments: p.arguments }));

  console.log("");
  console.log("=== " + nome + " ===");

  const r = spawnSync(NUOVA, [], { encoding: "utf8" });
  if (r.status !== 0) { console.log("  NO  parte nuova non creata"); return false; }

  await chiama("inspect_part");
  const fatto = await chiama("build", { steps: passi });
  if (fatto.errore) {
    console.log("  NO  il piano non e' passato: " +
                fatto.testo.replace(/[\r\n]+/g, " ").slice(0, 200));
    return false;
  }
  const volume = (fatto.dati || {}).volume_after_mm3;
  console.log("  ok  costruito, volume " + volume + " mm3");

  // ── lo stato, senza immagini: e' quello che il modello riceve OGGI ──
  const soloStato = await chiama("inspect_part");
  const stato = soloStato.dati || statoDalTesto(soloStato.testo);
  fs.writeFileSync(path.join(USCITA, nome + "-stato.json"),
                   JSON.stringify(stato, null, 1));
  const caratteri = fs.statSync(path.join(USCITA, nome + "-stato.json")).size;
  console.log("  ok  stato: " + caratteri + " caratteri, " +
              ((stato.faces || []).length) + " facce, " +
              ((stato.feature || []).length) + " lavorazioni");

  // ── le immagini ──
  const uno = await chiama("inspect_part", { views: PRIMA_MANO });
  const tre = await chiama("inspect_part", { views: SECONDA_MANO });
  const scritti = salvaImmagini(nome, PRIMA_MANO, uno.immagini)
    .concat(salvaImmagini(nome, SECONDA_MANO, tre.immagini));
  if (!scritti.length) {
    console.log("  NO  nessuna immagine: " +
                uno.testo.replace(/[\r\n]+/g, " ").slice(0, 200));
    return false;
  }
  console.log("  ok  " + scritti.join(", "));

  fs.writeFileSync(path.join(USCITA, nome + "-verita.txt"),
                   verita + "\nvolume misurato: " + volume + " mm3\n");
  return true;
}

(async function () {
  await manda("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "raccogli_viste", version: "1" },
  });
  avvisa("notifications/initialized");

  let bene = 0;
  for (const p of piani) {
    try { if (await raccogli(p)) bene++; }
    catch (e) { console.log("  NO  " + nomeDi(p) + ": " + (e && e.message)); }
  }

  console.log("");
  console.log(bene + " forme su " + piani.length + " raccolte in " + USCITA);
  ponte.stdin.end();
  process.exitCode = bene === piani.length ? 0 : 1;
})();
