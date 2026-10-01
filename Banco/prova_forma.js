// V3 — la frase basta a RIDISEGNARE il pezzo?
//
//   node prova_forma.js
//
// PERCHE' IL CRITERIO E' IL VOLUME E NON UN GIUDIZIO. La voce V3 chiede che
// dalla sola frase un modello sappia «ridisegnare il pezzo con le quote
// giuste». «Somiglia» non e' una misura: o il pezzo ridisegnato ha il volume
// dell'originale, o non ce l'ha.
//
// Il volume non e' tutto — due pezzi diversi possono averlo uguale — ma
// sbagliare una quota lo cambia quasi sempre, e la differenza dice **di
// quanto** si e' sbagliato invece di dire «no». Insieme all'ingombro, che si
// controlla anche quello, un falso positivo diventa improbabile.
//
// COME SI RIFA' TUTTA LA MISURA:
//   1. `node raccogli_viste.js`   costruisce i cinque pezzi veri e scrive
//                                 lo stato, che contiene il campo `shape`
//   2. si danno le cinque frasi a un modello ISOLATO, che scrive i piani in
//      `cases/forma/1..5.json` senza aver visto niente altro
//   3. `node prova_forma.js`      esegue i piani e confronta
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const RADICE = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(RADICE, "bin", "oce-solid-agent-mcp.exe");
const NUOVA = path.join(RADICE, "Controlli", "NuovaParte", "bin", "NuovaParte.exe");
const PIANI = path.join(__dirname, "cases", "shape");

// I PEZZI VERI, presi da `Banco/viste/*-stato.json`, che e' il file che il
// modello NON ha visto. Se manca, la misura non si puo' fare: non si inventa
// un valore atteso.
const VISTE = path.join(__dirname, "views");
const FORME = [
  "1_drilled_plate", "2_open_shell", "3_l_bracket",
  "4_drilled_flange", "5_stepped_shaft",
];

// Quanto puo' sbagliare il volume e valere lo stesso: l'1%.
// Piu' stretto rifiuterebbe un pezzo giusto disegnato con un raccordo in piu';
// piu' largo lascerebbe passare uno spessore sbagliato di un millimetro su
// dieci.
const TOLLERANZA = 0.01;

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
  const testo = (r.content || []).filter(c => c.type === "text")
                                 .map(c => c.text).join("\n");
  let dati = null;
  const i = testo.indexOf("{");
  if (i >= 0) { try { dati = JSON.parse(testo.slice(i)); } catch (e) {} }
  return { testo: testo, dati: dati, errore: !!r.isError };
}

// ──────────────────────────────────────────────────────────────────────────

let falliti = 0;
function segna(nome, ok, dettaglio) {
  if (!ok) falliti++;
  console.log((ok ? "  OK   " : "  NO   ") + nome.padEnd(26) + (dettaglio || ""));
}

function corto(s) {
  if (!s) return "(niente)";
  const uno = s.replace(/[\r\n]+/g, " ");
  return uno.length > 130 ? uno.slice(0, 130) + "..." : uno;
}

function vicini(a, b, tolleranza) {
  if (a == null || b == null) return false;
  const grande = Math.max(Math.abs(a), Math.abs(b), 1);
  return Math.abs(a - b) / grande <= tolleranza;
}

// L'ingombro non ha un ordine garantito — dipende da come il pezzo e' stato
// costruito — quindi si confronta ORDINATO: 200x15x120 e 120x200x15 sono lo
// stesso parallelepipedo visto da un'altra parte, e il modello non poteva
// sapere quale orientamento avesse scelto chi ha fatto l'originale.
function stessoIngombro(a, b) {
  if (!a || !b || a.length !== 3 || b.length !== 3) return false;
  const x = a.slice().sort((p, q) => p - q);
  const y = b.slice().sort((p, q) => p - q);
  return x.every((v, i) => vicini(v, y[i], TOLLERANZA));
}

async function provaUno(numero, nome) {
  const filePiano = path.join(PIANI, numero + ".json");
  if (!fs.existsSync(filePiano)) {
    segna("pezzo " + numero, false, "manca il piano " + filePiano);
    return;
  }
  const fileVero = path.join(VISTE, nome + "-stato.json");
  if (!fs.existsSync(fileVero)) {
    segna("pezzo " + numero, false,
          "manca lo stato vero: lancia prima `node raccogli_viste.js`");
    return;
  }
  const vero = JSON.parse(fs.readFileSync(fileVero, "utf8"));
  let passi;
  try { passi = JSON.parse(fs.readFileSync(filePiano, "utf8")); }
  catch (e) { segna("pezzo " + numero, false, "il piano non e' JSON: " + e.message); return; }
  if (!Array.isArray(passi)) { segna("pezzo " + numero, false, "il piano non e' un elenco"); return; }

  const r = spawnSync(NUOVA, [], { encoding: "utf8" });
  if (r.status !== 0) { segna("pezzo " + numero, false, "parte nuova non creata"); return; }

  await chiama("inspect_part");
  const fatto = await chiama("build", { steps: passi });
  if (fatto.errore) {
    segna("pezzo " + numero, false, "il piano non passa: " + corto(fatto.testo));
    return;
  }

  const rifatto = (fatto.dati || {}).state_after || {};
  const volumeRifatto = rifatto.volume_mm3;
  const volumeVero = vero.volume_mm3;
  const okVolume = vicini(volumeRifatto, volumeVero, TOLLERANZA);
  const okIngombro = stessoIngombro(rifatto.bounding_box_mm, vero.bounding_box_mm);

  const scarto = (volumeVero && volumeRifatto)
    ? ((volumeRifatto - volumeVero) / volumeVero * 100).toFixed(1) + "%"
    : "?";

  segna("pezzo " + numero + " " + nome.slice(2), okVolume && okIngombro,
        "volume " + (volumeRifatto == null ? "?" : Math.round(volumeRifatto * 10) / 10) +
        " contro " + volumeVero + " (" + scarto + ")" +
        (okIngombro ? "" : ", INGOMBRO diverso: " + JSON.stringify(rifatto.bounding_box_mm) +
                            " contro " + JSON.stringify(vero.bounding_box_mm)));
}

(async function () {
  await manda("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_forma", version: "1" },
  });
  avvisa("notifications/initialized");

  console.log("");
  console.log("=== la frase basta a ridisegnare il pezzo? (V3) ===");
  console.log("");

  for (let i = 0; i < FORME.length; i++) {
    try { await provaUno(String(i + 1), FORME[i]); }
    catch (e) { segna("pezzo " + (i + 1), false, "si e' rotto: " + (e && e.message)); }
  }

  console.log("");
  console.log(falliti === 0
    ? "Tutti e cinque ridisegnati dalla sola frase."
    : falliti + " pezzi su " + FORME.length + " NON si ridisegnano dalla sola frase.");
  console.log("Il numero va nel README accanto a quello di V2: sono due modi di " +
              "dire la stessa cosa, e costano ordini di grandezza diversi.");
  ponte.stdin.end();
  process.exitCode = 0;   // e' una MISURA, non un controllo: non fa fallire un giro
})();
