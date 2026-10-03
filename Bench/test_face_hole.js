// E14 — `hole_face` prende un foro, e non un raccordo.
//
//   node test_face_hole.js
//
// PERCHE' QUI E NON NEL BANCO. `face_ref: {passo, genere}` nomina un passo
// dello STESSO piano, e il banco esegue un'operazione per volta: li' il
// riferimento non si puo' nemmeno formulare. La prima scrittura di questa prova
// stava nel banco ed e' uscita rossa per il motivo sbagliato — «di quel passo
// non ho la geometria» — senza mai arrivare al codice che doveva provare.
//
// Un rosso che scatta per un'altra ragione non prova niente, e sembra identico
// a uno che funziona.
//
// ── IL DIFETTO ────────────────────────────────────────────────────────────
//
// `Riferimenti` cercava le facce «cilindriche» del passo: una sola la
// restituiva, piu' d'una faceva scattare un rifiuto. Ma **un raccordo lascia
// facce cilindriche come un foro**. Un passo che ne lasciava esattamente una, e
// quella era un raccordo, la restituiva senza dire niente.
//
// Il criterio e' geometrico e misurato (`Checks\HoleOrFillet`): la
// parete di un foro e' CONCAVA, la superficie di un raccordo esterno e'
// convessa.
const { spawn, spawnSync } = require("child_process");
const path = require("path");

const RADICE = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(RADICE, "bin", "oce-solid-agent-mcp.exe");
const NUOVA = path.join(RADICE, "Checks", "NewPart", "bin", "NewPart.exe");

const PIASTRA = {
  operation: "extrude_rectangle",
  arguments: {
    plane: "top", length_mm: 100, width_mm: 60,
    thickness_mm: 20, centered: true,
  },
};

// IL RUOLO E' RAGGIUNGIBILE DA UNA SOLA OPERAZIONE, e va detto: `shell`.
// Verificato sul catalogo — `hole_face` compare nello schema di `shell`
// e in nessun altro. La prima scrittura di questa prova usava
// `cylindrical_cut`, che quel genere non lo accetta nemmeno, e il rifiuto
// arrivava dal validatore dello schema invece che dal codice da provare: il
// secondo rosso che scattava per il motivo sbagliato, nella stessa ora.

// ROSSO 1: il passo 2 e' un RACCORDO, che lascia facce cilindriche CONVESSE.
// Il passo 3 chiede «la parete del foro» di quel passo: non esiste.
const RACCORDO_NON_E_FORO = [
  PIASTRA,
  {
    operation: "fillet_edges",
    arguments: {
      edges_ref: { step: 1, kind: "side_edges" },
      radius_mm: 5,
    },
  },
  {
    operation: "shell",
    arguments: {
      faces_ref: [{ step: 2, kind: "hole_face" }],
      thickness_mm: 4,
    },
  },
];

// VERDE: il passo 2 e' un foro vero, uno solo. Il passo 3 lo prende.
const IL_FORO_SI_PRENDE = [
  PIASTRA,
  {
    operation: "cylindrical_cut",
    arguments: {
      face_ref: { step: 1, kind: "top_face" },
      x_mm: 0, y_mm: 0, diameter_mm: 30, through_all: true,
    },
  },
  {
    operation: "shell",
    arguments: {
      faces_ref: [{ step: 2, kind: "hole_face" }],
      thickness_mm: 4,
    },
  },
];

// ROSSO 2: un passo che lascia PIU' pareti di foro. «La» parete non esiste, e
// sceglierne una sarebbe indovinare: il rifiuto e' il comportamento giusto, non
// un limite da riparare.
//
// SERVE UN PASSO SOLO CHE FACCIA PIU' FORI, e trovarlo e' costato tre
// tentativi sbagliati:
//
//   * `cylindrical_cut` fa un foro per passo: nessuna ambiguita';
//   * `linear_pattern` ne fa quattro, ma vuole una direzione data da un asse o
//     da uno spigolo — cioe' da un uid che dentro questo piano non esiste
//     ancora, perche' la piastra la crea il passo 1;
//   * `mirror` ne fa uno nuovo, e l'originale appartiene al passo di prima:
//     il passo dello specchio ha **una sola** parete, e prenderla e' giusto.
//
// `hole_wizard` invece lo dice da solo nello schema: «piu' fori uguali stanno
// in una chiamata sola». Quattro fori, un passo, quattro pareti.
//
// La lezione che e' costata i tre tentativi: **un caso di prova non deve
// dipendere da geometria che il caso stesso sta creando**, e «quale operazione
// lascia N facce» si legge nel catalogo invece di dedurlo.
const PIU_PARETI = [
  PIASTRA,
  {
    operation: "hole_wizard",
    arguments: {
      face_ref: { step: 1, kind: "top_face" },
      size: "M10",
      through_all: true,
      positions: [
        { x_mm: -30, y_mm: -15 }, { x_mm: 30, y_mm: -15 },
        { x_mm: 30, y_mm: 15 }, { x_mm: -30, y_mm: 15 },
      ],
    },
  },
  {
    operation: "shell",
    arguments: {
      faces_ref: [{ step: 2, kind: "hole_face" }],
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
  const testo = (r.content || []).filter(c => c.type === "text")
                                 .map(c => c.text).join("\n");
  let dati = null;
  const i = testo.indexOf("{");
  if (i >= 0) { try { dati = JSON.parse(testo.slice(i)); } catch (e) {} }
  return { testo: testo, dati: dati, errore: !!r.isError };
}

let falliti = 0;
function segna(nome, ok, dettaglio) {
  if (!ok) falliti++;
  console.log((ok ? "  OK   " : "  NO   ") + nome.padEnd(52) + (dettaglio || ""));
}

function corto(s) {
  if (!s) return "(niente)";
  const uno = s.replace(/[\r\n]+/g, " ");
  return uno.length > 150 ? uno.slice(0, 150) + "..." : uno;
}

// L'ERRORE DEL PASSO, non il testo intero della risposta: dentro c'e' anche lo
// stato del documento, e cercarci una parola dentro darebbe falsi positivi.
function erroreDeiPassi(dati) {
  if (!dati || !Array.isArray(dati.outcomes)) return "";
  return dati.outcomes.filter(e => e && e.errore).map(e => e.errore).join(" | ");
}

async function piano(nome, passi) {
  const r = spawnSync(NUOVA, [], { encoding: "utf8" });
  if (r.status !== 0) { segna(nome, false, "parte nuova non creata"); return null; }
  await chiama("inspect_part");
  return await chiama("build", { steps: passi });
}

(async function () {
  await manda("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_faccia_foro", version: "1" },
  });
  avvisa("notifications/initialized");

  console.log("");
  console.log("=== hole_face: un foro si', un raccordo no (E14) ===");
  console.log("");

  // ── ROSSO 1 ────────────────────────────────────────────────────────────
  const a = await piano("ROSSO 1", RACCORDO_NON_E_FORO);
  if (a) {
    const perche = erroreDeiPassi(a.dati);
    segna("ROSSO: «la parete del foro» di un RACCORDO e' rifiutata",
          a.errore && /convess/i.test(perche),
          a.errore ? corto(perche) : "NON ha rifiutato: ha preso la faccia del raccordo");
  }

  // ── VERDE ──────────────────────────────────────────────────────────────
  const b = await piano("VERDE", IL_FORO_SI_PRENDE);
  if (b) {
    segna("un foro vero, uno solo, si prende",
          !b.errore,
          b.errore ? corto(erroreDeiPassi(b.dati)) :
                     "volume " + (b.dati || {}).volume_after_mm3);
  }

  // ── ROSSO 2 ────────────────────────────────────────────────────────────
  const c = await piano("ROSSO 2", PIU_PARETI);
  if (c) {
    const perche = erroreDeiPassi(c.dati);
    segna("ROSSO: piu' pareti di foro, «la» parete non esiste",
          c.errore && /pareti di foro/i.test(perche),
          c.errore ? corto(perche) : "NON ha rifiutato: ne ha scelta una a caso");
  }

  console.log("");
  console.log(falliti === 0 ? "Tutto torna." : falliti + " prove fallite.");
  ponte.stdin.end();
  process.exitCode = falliti === 0 ? 0 : 1;
})();
