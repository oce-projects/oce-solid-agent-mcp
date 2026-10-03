// Prova lo strumento `undo` (voce R1) parlando al ponte come un client MCP.
//
//   node prova_annulla.js
//
// PERCHE' QUI E NON NEL BANCO. `ProvaSaldatura` prova la LIBRERIA per
// riflessione; `undo` non sta nella libreria — e' uno strumento del ponte,
// e la parte che conta (il ricordo dell'ultimo piano, i rifiuti) vive in
// `Attrezzi`. Provarlo dalla libreria vorrebbe dire provare un'altra cosa.
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

const RADICE = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(RADICE, "bin", "oce-solid-agent-mcp.exe");
const NUOVA = path.join(RADICE, "Controlli", "NuovaParte", "bin", "NuovaParte.exe");

const PIANO_DUE_PASSI = [
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

const PIANO_UN_PASSO = [
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

let prossimoId = 1;
const attesa = new Map();
let avanzo = "";

const ponte = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
ponte.stderr.on("data", () => {});   // i messaggi di servizio non sono risposte
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

// Il testo di una risposta, piu' `isError`. Un'operazione che fallisce non e'
// un errore di protocollo: sta nel contenuto, ed e' li' che va guardata.
async function chiama(strumento, argomenti) {
  const m = await manda("tools/call",
    { name: strumento, arguments: argomenti || {} });
  const r = m.result || {};
  const testo = (r.content || []).map(c => c.text).filter(Boolean).join("\n");
  let dati = null; try { dati = JSON.parse(testo); } catch (e) {}
  return { testo: testo, dati: dati, errore: !!r.isError };
}

// ──────────────────────────────────────────────────────────────────────────

let falliti = 0;
function segna(nome, ok, dettaglio) {
  if (!ok) falliti++;
  console.log((ok ? "  OK   " : "  NO   ") + nome.padEnd(56) + (dettaglio || ""));
}

function corto(s) {
  if (!s) return "(niente)";
  const uno = s.replace(/[\r\n]+/g, " ");
  return uno.length > 100 ? uno.slice(0, 100) + "..." : uno;
}

function pezzoPulito() {
  const r = spawnSync(NUOVA, [], { encoding: "utf8" });
  return r.status === 0;
}

async function giro() {
  await manda("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_annulla", version: "1" },
  });
  avvisa("notifications/initialized");

  // Lo strumento deve ESSERCI, e dichiararsi per quello che fa.
  const elenco = await manda("tools/list", {});
  const strumenti = (elenco.result || {}).tools || [];
  const nomi = strumenti.map(function (t) { return t.name; });
  segna("annulla e' fra gli strumenti", nomi.indexOf("annulla") >= 0,
        nomi.join(", "));
  if (nomi.indexOf("annulla") < 0) return;

  const suo = strumenti.filter(function (t) { return t.name === "undo"; })[0] || {};
  const ann = suo.annotations || {};
  segna("annulla si dichiara distruttivo e non idempotente",
        ann.destructiveHint === true && ann.idempotentHint === false &&
        ann.readOnlyHint === false,
        JSON.stringify(ann));

  // ── ROSSO 1: niente da ritirare ────────────────────────────────────────
  // Questa e' la PRIMA chiamata della conversazione: il ponte non ha eseguito
  // nessun piano, quindi non sa cosa toglierebbe.
  const vuoto = await chiama("undo");
  segna("ROSSO 1: senza nessun piano, rifiuta",
        vuoto.errore && /nessun piano da ritirare/i.test(vuoto.testo),
        corto(vuoto.testo));

  if (!pezzoPulito()) {
    segna("una parte pulita", false, "NuovaParte non ha risposto");
    return;
  }

  const visto = await chiama("inspect_part");
  segna("il pezzo pulito si legge", !visto.errore && visto.dati != null,
        visto.dati ? "volume " + visto.dati.volume_mm3 : corto(visto.testo));

  // ── IL VERDE: due passi, ritirati insieme ──────────────────────────────
  const fatto = await chiama("build", { steps: PIANO_DUE_PASSI });
  const volumeDopoIlPiano = fatto.dati ? fatto.dati.volume_after_mm3 : null;
  segna("il piano di due passi costruisce", !fatto.errore && volumeDopoIlPiano > 0,
        fatto.errore ? corto(fatto.testo) : "volume " + volumeDopoIlPiano);
  if (fatto.errore) return;

  const ritirato = await chiama("undo");
  const d = ritirato.dati || {};
  segna("annulla ritira il piano intero",
        !ritirato.errore && d.undone === true,
        ritirato.errore ? corto(ritirato.testo) : "undone");
  segna("il volume torna a zero",
        d.volume_after_mm3 != null && Math.abs(d.volume_after_mm3) < 0.001,
        "volume " + d.volume_after_mm3 + " (era " + d.volume_before_mm3 + ")");
  segna("le lavorazioni tornano a zero",
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
  segna("la risposta porta lo stato di adesso",
        d.state_after != null &&
        typeof d.state_after.document_title === "string" &&
        ("solid_body" in d.state_after),
        d.state_after ? "titolo «" + d.state_after.document_title + "», solid_body " +
                       d.state_after.solid_body
                     : "assente");

  // ── ROSSO 2: due volte di fila ─────────────────────────────────────────
  // Il primo annullamento ha consumato il ricordo. Il secondo toglierebbe
  // quello che c'era PRIMA del piano, che questo ponte non ha fatto.
  const bis = await chiama("undo");
  segna("ROSSO 2: la seconda volta rifiuta",
        bis.errore && /nessun piano da ritirare/i.test(bis.testo),
        corto(bis.testo));

  // ── ROSSO 3: un altro documento davanti ────────────────────────────────
  const altro = await chiama("build", { steps: PIANO_UN_PASSO });
  segna("un secondo piano costruisce", !altro.errore,
        altro.errore ? corto(altro.testo)
                     : "volume " + (altro.dati || {}).volume_after_mm3);
  if (altro.errore) return;

  if (!pezzoPulito()) {
    segna("ROSSO 3: con un altro documento davanti, rifiuta", false,
          "NuovaParte non ha risposto");
    return;
  }
  const sbagliato = await chiama("undo");
  segna("ROSSO 3: con un altro documento davanti, rifiuta",
        sbagliato.errore && /documento e' cambiato/i.test(sbagliato.testo),
        corto(sbagliato.testo));
}

giro()
  .then(function () {
    console.log("");
    console.log(falliti === 0 ? "Tutto torna." : falliti + " prove fallite.");
    ponte.stdin.end();
    process.exitCode = falliti === 0 ? 0 : 1;
  })
  .catch(function (e) {
    console.error("il giro si e' rotto: " + (e && e.message));
    ponte.stdin.end();
    process.exitCode = 2;
  });
