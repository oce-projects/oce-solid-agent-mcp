// Le prove dell'installatore che si possono fare su una macchina qualunque.
//
//     node Controlli\prova-installatore.js
//
// Esce 0 se passano tutte, 1 se no.
//
// NON SONO TUTTE E SEI, E LE ALTRE TRE SONO ELENCATE ALLA FINE invece di
// essere dichiarate passate. Il caso «SOLIDWORKS assente» vuole una macchina
// senza SOLIDWORKS; «installazione pulita» nella sua forma piena ne vuole una
// senza il ponte; «client aperto» vuole Claude Desktop che tiene il file.
// Quelle si fanno a mano, e il loro esito va scritto in PROVE-INSTALLATORE.md.
//
// IL CASO 6 E' IL PIU' IMPORTANTE, e si puo' fare tutto qui: «ci sono gia'
// altri server MCP, restano intatti?». Non produce nessun sintomo subito — il
// file sembra scritto bene finche' qualcuno non riapre il client il giorno dopo
// e trova sparito il server che usava per lavorare.
//
// Si confronta la STRUTTURA, non il testo: se il JSON di prova ha spaziatura
// diversa da quella che l'installatore produce, un diff dei file interi
// segnalerebbe un fallimento che non c'e'.
const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const os = require("os");

const QUI = __dirname;
const RADICE = path.dirname(QUI);
const INSTALLA = process.env.INSTALLA || path.join(RADICE, "dist", "Installa.exe");
const MODELLO = path.join(QUI, "config-con-altri-server.json");

// Si lavora in una cartella temporanea: la configurazione VERA di chi sta
// provando non si tocca, e non ci si affida al fatto di ricordarsene.
const BANCO = fs.mkdtempSync(path.join(os.tmpdir(), "prova-installatore-"));

function installa(args) {
  const r = spawnSync(INSTALLA, args, { encoding: "utf8", maxBuffer: 32 * 1024 * 1024 });
  return { uscita: r.status, testo: (r.stdout || "") + (r.stderr || "") };
}

const esiti = [];
function esito(caso, passa, dettaglio) {
  esiti.push({ caso, passa });
  console.log("  " + (passa ? "PASS" : "FAIL") + "  " + caso);
  if (dettaglio) console.log("        " + String(dettaglio).replace(/\n/g, "\n        "));
}

if (!fs.existsSync(INSTALLA)) {
  console.error("non trovo " + INSTALLA + " — compila prima con build.ps1");
  process.exit(2);
}
console.log("installatore: " + INSTALLA);
console.log("banco:        " + BANCO);

const uguale = (a, b) => JSON.stringify(a) === JSON.stringify(b);

// ── CASO 6 — gli altri server restano ────────────────────────────────────
console.log("\n=== CASO 6 - con due server MCP gia' presenti");
const cfg = path.join(BANCO, "claude_desktop_config.json");
fs.copyFileSync(MODELLO, cfg);
const prima = JSON.parse(fs.readFileSync(cfg, "utf8"));
const cartella = path.join(BANCO, "installato");

installa(["--silenzioso", "--cartella", cartella, "--config", cfg]);

let dopo = null;
try { dopo = JSON.parse(fs.readFileSync(cfg, "utf8")); } catch (e) { }
esito("6. il config resta JSON valido", !!dopo, dopo ? null : "non si rilegge");

if (dopo) {
  for (const nome of ["filesystem", "github"]) {
    const d = (dopo.mcpServers || {})[nome];
    esito("6. '" + nome + "' e' ancora identico",
      !!d && uguale(prima.mcpServers[nome], d),
      d ? null : "SPARITO");
  }
  esito("6. e' comparso 'solidworks'", !!(dopo.mcpServers || {}).solidworks, null);
  esito("6. non e' comparso nient'altro",
    Object.keys(dopo.mcpServers || {}).sort().join(",") === "filesystem,github,solidworks",
    Object.keys(dopo.mcpServers || {}).sort().join(", "));
  esito("6. le impostazioni del client fuori da mcpServers sono intatte",
    uguale(prima.altreImpostazioniDelClient, dopo.altreImpostazioniDelClient), null);
}

// ── CASO 2 — installare sopra una precedente ─────────────────────────────
console.log("\n=== CASO 2 - installazione sopra una precedente");
const r2 = installa(["--silenzioso", "--cartella", cartella, "--config", cfg]);
let dopo2 = null;
try { dopo2 = JSON.parse(fs.readFileSync(cfg, "utf8")); } catch (e) { }

esito("2. la seconda installazione non fallisce", r2.uscita === 0,
  r2.uscita === 0 ? null : "uscita " + r2.uscita + "\n" + r2.testo.slice(-400));
esito("2. la voce 'solidworks' e' aggiornata, non duplicata",
  !!dopo2 && Object.keys(dopo2.mcpServers || {}).sort().join(",") === "filesystem,github,solidworks",
  dopo2 ? Object.keys(dopo2.mcpServers || {}).sort().join(", ") : "config illeggibile");

const bak = fs.readdirSync(BANCO).filter(f => /\.bak$/i.test(f));
esito("2. c'e' una copia di sicurezza del config", bak.length > 0, bak.join("\n"));

// ── CASO 3 — disinstallazione ────────────────────────────────────────────
console.log("\n=== CASO 3 - disinstallazione");
const estraneo = path.join(cartella, "un-mio-log.txt");
fs.writeFileSync(estraneo, "roba messa da chi usa il programma\n");
installa(["--disinstalla", "--cartella", cartella]);

const restano = fs.existsSync(cartella) ? fs.readdirSync(cartella) : null;
esito("3. la cartella NON viene cancellata se dentro resta roba d'altri",
  restano !== null && restano.includes("un-mio-log.txt"),
  restano === null ? "cartella cancellata con dentro un file di chi usa il programma"
                   : "resta: " + restano.join(", "));
esito("3. il ponte e' sparito",
  restano !== null && !restano.includes("SolidWorksMcp.exe"),
  restano === null ? "n/d" : restano.join(", "));

// ── riepilogo ────────────────────────────────────────────────────────────
const falliti = esiti.filter(e => !e.passa);
console.log("\n=== RIEPILOGO");
console.log("  " + (esiti.length - falliti.length) + " PASS, " + falliti.length + " FAIL");
console.log("\n  NON eseguite da questo comando, e vanno fatte a mano:");
console.log("    1. installazione su macchina pulita  (VM con SOLIDWORKS, senza il ponte)");
console.log("    4. client aperto durante l'installazione  (Claude Desktop che tiene il file)");
console.log("    5. SOLIDWORKS assente  (macchina senza SOLIDWORKS)");
console.log("\n  banco lasciato in " + BANCO + " se serve guardarci dentro");
process.exit(falliti.length ? 1 : 0);
