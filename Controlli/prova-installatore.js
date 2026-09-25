// Le prove dell'installatore che si possono fare su una macchina qualunque.
//
//     node Controlli\prova-installatore.js
//
// Esce 0 se passano tutte, 1 se no.
//
// Lavora in una cartella temporanea: LA CONFIGURAZIONE VERA DI CHI PROVA NON
// SI TOCCA, e non ci si affida al fatto di ricordarsene.
//
// GLI SCENARI SONO SEI, E QUESTO NE ESEGUE TRE E MEZZO.
//
//   | # | prova                                   | qui        |
//   |---|-----------------------------------------|------------|
//   | 1 | installazione su macchina pulita        | no: VM con SOLIDWORKS e senza il ponte |
//   | 2 | installazione sopra una precedente      | **si'**    |
//   | 3 | disinstallazione                        | **si'**    |
//   | 4 | client aperto durante l'installazione   | **meta'**: il file tenuto da un altro processo si'; Claude Desktop vero no |
//   | 5 | SOLIDWORKS assente                      | no: serve una macchina senza SOLIDWORKS |
//   | 6 | config con altri server MCP gia' presenti | **si'**  |
//
// Le altre tre si fanno a mano su una macchina che qui non c'e'. Vanno
// elencate come NON ESEGUITE ovunque se ne riporti l'esito: *una prova non
// fatta si scrive, non si dichiara passata.*
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

// H15: chi reinstalla sa se sta aggiornando o rimettendo la stessa. La
// versione attesa si legge da VERSION.txt, non si scrive qui: cambierebbe a
// ogni rilascio e il caso diventerebbe rosso per la ragione sbagliata.
const versione = fs.readFileSync(path.join(RADICE, "VERSION.txt"), "utf8").trim();
esito("2. dice che reinstalla la stessa versione, e quale",
  r2.testo.includes("reinstallo la " + versione),
  (r2.testo.match(/^.*(installo|aggiorno).*$/mi) || ["nessuna riga di versione"])[0].trim());

// H15: le copie di sicurezza si potano. Prima non si toglievano mai: dieci
// installazioni, dieci .bak in %APPDATA%. Il nome porta i secondi, quindi fra
// un giro e l'altro si aspetta un secondo, se no due copie avrebbero lo stesso
// nome e il conto non proverebbe niente.
const aspetta = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
for (let i = 0; i < 4; i++) {
  aspetta(1100);
  installa(["--silenzioso", "--cartella", cartella, "--config", cfg]);
}
const bakDopo = fs.readdirSync(BANCO).filter(f => /\.prima-di-oce-solid-agent-.*\.bak$/i.test(f));
esito("2. dopo sei installazioni le copie di sicurezza sono al piu' tre",
  bakDopo.length > 0 && bakDopo.length <= 3, bakDopo.length + " copie: " + bakDopo.join(", "));

// ── CASO 4 — il config tenuto da un altro processo ──────────────────────
//
// Non e' il caso 4 intero (Claude Desktop vero che tiene il file e magari lo
// riscrive uscendo): e' la meta' che si puo' fare qui. Un PowerShell figlio
// apre il file in esclusiva e lo tiene; l'installatore deve fermarsi SENZA
// averlo toccato — niente `.parziale` lasciato accanto, niente copia nuova —
// e dirlo con una parola che faccia capire cosa fare.
console.log("\n=== CASO 4 (meta') - il config bloccato da un altro processo");
const cfg4 = path.join(BANCO, "bloccato.json");
fs.copyFileSync(MODELLO, cfg4);
const byte4 = fs.readFileSync(cfg4);
const segnale = path.join(BANCO, "bloccato.pronto");
const tieni = require("child_process").spawn("powershell", ["-NoProfile", "-Command",
  "$f=[System.IO.File]::Open('" + cfg4 + "','Open','ReadWrite','None'); " +
  "Set-Content -Path '" + segnale + "' -Value ok; Start-Sleep -Seconds 60; $f.Close()"],
  { stdio: "ignore" });
for (let i = 0; i < 100 && !fs.existsSync(segnale); i++) aspetta(100);
if (!fs.existsSync(segnale)) {
  esito("4. il file si riesce a bloccare per la prova", false, "il PowerShell figlio non ha segnalato");
} else {
  const bakPrima4 = fs.readdirSync(BANCO).filter(f => f.startsWith("bloccato.json.")).length;
  const r4 = installa(["--silenzioso", "--cartella", path.join(BANCO, "installato4"), "--config", cfg4]);
  tieni.kill();
  aspetta(500);
  const resti = fs.readdirSync(BANCO).filter(f => f.startsWith("bloccato.json."));
  esito("4. dice che il file e' bloccato", /bloccato/i.test(r4.testo),
    (r4.testo.match(/^.*bloccat.*$/mi) || [r4.testo.slice(-300)])[0].trim());
  esito("4. il config e' rimasto identico, byte per byte",
    Buffer.compare(byte4, fs.readFileSync(cfg4)) === 0, null);
  esito("4. non resta un .parziale e non nasce una copia",
    resti.length === bakPrima4, resti.join(", ") || "niente accanto");
}

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
  restano !== null && !restano.includes("oce-solid-agent-mcp.exe"),
  restano === null ? "n/d" : restano.join(", "));
// H15: installazione e disinstallazione leggono la stessa lista. Quello che
// resta dev'essere SOLO la roba di chi usa il programma.
esito("3. non resta nessun file dell'installazione",
  restano !== null && restano.length === 1 && restano[0] === "un-mio-log.txt",
  restano === null ? "n/d" : "resta: " + restano.join(", "));

// ── riepilogo ────────────────────────────────────────────────────────────
const falliti = esiti.filter(e => !e.passa);
console.log("\n=== RIEPILOGO");
console.log("  " + (esiti.length - falliti.length) + " PASS, " + falliti.length + " FAIL");
console.log("\n  NON eseguite da questo comando, e vanno fatte a mano:");
console.log("    1. installazione su macchina pulita  (VM con SOLIDWORKS, senza il ponte)");
console.log("    4. client aperto durante l'installazione  (Claude Desktop vero: qui c'e' solo la meta'");
console.log("       del file bloccato da un altro processo; se il client lo riscrive uscendo, non si sa)");
console.log("    5. SOLIDWORKS assente  (macchina senza SOLIDWORKS)");
console.log("\n  banco lasciato in " + BANCO + " se serve guardarci dentro");
process.exit(falliti.length ? 1 : 0);
