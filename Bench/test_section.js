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

const RADICE = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(RADICE, "bin", "oce-solid-agent-mcp.exe");
const NUOVA = path.join(RADICE, "Checks", "NewPart", "bin", "NewPart.exe");
const USCITA = path.join(__dirname, "viste");

// Lo stesso guscio della misura V2/V3: scatola 120x80x50, parete 4, aperta in
// alto. Dall'esterno e' un parallelepipedo; la parete si vede solo dentro.
const GUSCIO = [
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
  let dati = null;
  const i = testo.indexOf("{");
  if (i >= 0) { try { dati = JSON.parse(testo.slice(i)); } catch (e) {} }
  return { testo: testo, dati: dati, immagini: immagini, errore: !!r.isError };
}

let falliti = 0;
function segna(nome, ok, dettaglio) {
  if (!ok) falliti++;
  console.log((ok ? "  OK   " : "  NO   ") + nome.padEnd(46) + (dettaglio || ""));
}

function corto(s) {
  if (!s) return "(niente)";
  const uno = s.replace(/[\r\n]+/g, " ");
  return uno.length > 120 ? uno.slice(0, 120) + "..." : uno;
}

function salva(nome, immagine) {
  const file = path.join(USCITA, nome + ".png");
  fs.writeFileSync(file, Buffer.from(immagine.data, "base64"));
  return file;
}

(async function () {
  await manda("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_sezione", version: "1" },
  });
  avvisa("notifications/initialized");
  fs.mkdirSync(USCITA, { recursive: true });

  console.log("");
  console.log("=== lo spaccato mostra la parete? (V4) ===");
  console.log("");

  const r = spawnSync(NUOVA, [], { encoding: "utf8" });
  if (r.status !== 0) { segna("una parte pulita", false, "NuovaParte non risponde"); return; }

  await chiama("inspect_part");
  const fatto = await chiama("build", { steps: GUSCIO });
  if (fatto.errore) { segna("il guscio", false, corto(fatto.testo)); ponte.stdin.end(); return; }
  segna("il guscio si costruisce", true,
        "volume " + (fatto.dati || {}).volume_after_mm3);

  // ── com'era prima, per poterlo confrontare dopo ────────────────────────
  const prima = await chiama("inspect_part", { views: ["isometric"] });
  if (!prima.immagini.length) { segna("la vista intera", false, corto(prima.testo)); ponte.stdin.end(); return; }
  const fileIntero = salva("v4-guscio-intero", prima.immagini[0]);
  const statoPrima = prima.dati || {};
  segna("la vista intera esce", true, path.basename(fileIntero));

  // ── lo spaccato ────────────────────────────────────────────────────────
  const tagliato = await chiama("inspect_part", {
    section: { plane: "right", offset_mm: 0 },
  });
  if (!tagliato.immagini.length) {
    segna("la sezione esce", false, corto(tagliato.testo));
  } else {
    const fileSezione = salva("v4-guscio-sezione", tagliato.immagini[0]);
    segna("la sezione esce", true, path.basename(fileSezione));

    // IL ROSSO: una sezione che non seziona da' la STESSA immagine, e
    // «la sezione esce» passerebbe lo stesso.
    const a = fs.readFileSync(fileIntero);
    const b = fs.readFileSync(fileSezione);
    segna("la sezione e' DIVERSA dalla vista intera",
          !a.equals(b),
          a.equals(b) ? "identiche: non ha sezionato niente"
                      : a.length + " byte contro " + b.length);
  }

  // ── e il pezzo di chi lavora e' come prima ─────────────────────────────
  const dopo = await chiama("inspect_part");
  const s = dopo.dati || {};
  segna("il pezzo non e' cambiato",
        s.volume_mm3 === statoPrima.volume_mm3 &&
        s.bodies_total === statoPrima.bodies_total &&
        (s.feature || []).length === (statoPrima.feature || []).length,
        "volume " + s.volume_mm3 + ", corpi " + s.bodies_total +
        ", lavorazioni " + (s.feature || []).length);

  console.log("");
  console.log("Le due immagini sono in " + USCITA + ":");
  console.log("  v4-guscio-intero.png    v4-guscio-sezione.png");
  console.log("Guardarle e' l'altra meta' del criterio: dalla sezione si deve " +
              "LEGGERE lo spessore della parete.");
  console.log("");
  console.log(falliti === 0 ? "Tutto torna." : falliti + " prove fallite.");
  ponte.stdin.end();
  process.exitCode = falliti === 0 ? 0 : 1;
})();
