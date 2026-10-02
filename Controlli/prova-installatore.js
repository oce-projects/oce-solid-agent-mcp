// Le prove dell'installatore che si possono fare su una macchina qualunque.
//
//     node Controlli\prova-installatore.js            (SOLIDWORKS not touched)
//     node Controlli\prova-installatore.js --attach   (the real session: bench stopped)
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

// SOLIDWORKS IS NOT TOUCHED UNLESS ASKED (entry H19, L1). The installer runs
// the installed bridge after copying it (the self-test, and with --verify an
// MCP handshake); both used to attach to the running SOLIDWORKS session and
// read its active document, which is why this script could only run with the
// bench stopped. OCE_NO_ATTACH makes the bridge start without attaching, and
// its self-test then exits 5, which the installer accepts like "SOLIDWORKS is
// not running". `--attach` runs everything against the real session: that is
// the run the owner does with the bench stopped, and the only one in which
// --verify can come out all green (exit 0).
const ATTACH = process.argv.includes("--attach");
const ENV = Object.assign({}, process.env);
if (ATTACH) delete ENV.OCE_NO_ATTACH; else ENV.OCE_NO_ATTACH = "1";

function installa(args, opts) {
  opts = opts || {};
  const r = spawnSync(INSTALLA, args, {
    encoding: "utf8", maxBuffer: 32 * 1024 * 1024,
    env: Object.assign({}, ENV, opts.env || {}),
    // An old installer that does not know an option opens its window and
    // waits for a click: the timeout turns that into a red line instead of
    // a run that never ends.
    timeout: opts.timeout || 120000,
  });
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

const r6 = installa(["--silenzioso", "--cartella", cartella, "--config", cfg]);
// H19 B1: 0 means installed AND the client wired.
esito("6. exit code 0: installed and the client wired (H19 B1)", r6.uscita === 0,
  "exit " + r6.uscita + "\n" + r6.testo.slice(-400));

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

// ═════════════════════════════════════════════════════════════════════════
//  ENTRY H19, LOTS L1 AND L2. Every case below was run red on the installer
//  of 47d0f8f (c7ab519 plus OCE_NO_ATTACH only) before it was run green.
// ═════════════════════════════════════════════════════════════════════════

const read = f => { try { return fs.readFileSync(f, "utf8"); } catch (e) { return null; } };
const json = f => { try { return JSON.parse(fs.readFileSync(f, "utf8")); } catch (e) { return null; } };
// The line before the block to paste is looked for as a literal, where the
// inventory of consumed strings (Controlli/lingua/consumed-strings.json) can
// find it: installer-paste-header.

// ── B1 — the exit code tells the truth ───────────────────────────────────
console.log("\n=== H19 B1 - exit codes");
const cfgBad = path.join(BANCO, "malformed.json");
fs.writeFileSync(cfgBad, "{ \"mcpServers\": { this is not JSON\n");
const byteBad = fs.readFileSync(cfgBad);
const logBad = path.join(BANCO, "malformed.log");
const rBad = installa(["--silenzioso", "--cartella", path.join(BANCO, "installed-bad"), "--config", cfgBad, "--log", logBad]);
esito("B1. a malformed client config: exit 3 (files installed, no client wired)", rBad.uscita === 3,
  "exit " + rBad.uscita + "\n" + rBad.testo.slice(-300));
esito("B1. the malformed config is left byte for byte as it was",
  Buffer.compare(byteBad, fs.readFileSync(cfgBad)) === 0, null);
esito("B1. exit 3 prints the block to paste by hand",
  rBad.testo.includes("Paste this into") && /"solidworks"/.test(rBad.testo),
  (rBad.testo.match(/^.*Paste.*$/m) || ["no paste line"])[0]);

const rNone = installa(["--silenzioso", "--senza-client", "--cartella", path.join(BANCO, "installed-none")]);
esito("B1. --senza-client: exit 0 (no client asked for)", rNone.uscita === 0,
  "exit " + rNone.uscita + "\n" + rNone.testo.slice(-300));

const emptyRedist = path.join(BANCO, "empty-redist");
fs.mkdirSync(emptyRedist);
const neverInstalled = path.join(BANCO, "never-installed");
const rNo = installa(["--silenzioso", "--redist", emptyRedist, "--cartella", neverInstalled, "--config", path.join(BANCO, "never.json")]);
esito("B1. a prerequisite fails: exit 1 and nothing installed",
  rNo.uscita === 1 && !fs.existsSync(neverInstalled) && !fs.existsSync(path.join(BANCO, "never.json")),
  "exit " + rNo.uscita + ", folder " + (fs.existsSync(neverInstalled) ? "created" : "absent"));

// ── A7 — stale texts ─────────────────────────────────────────────────────
console.log("\n=== H19 A7 - stale texts");
esito("A7. a missing SOLIDWORKS in silent mode names --redist, not a Browse button",
  !/Sfoglia/.test(rNo.testo) && rNo.testo.includes("--redist"),
  (rNo.testo.match(/^.*SOLIDWORKS.*$/m) || [""])[0] + "\n" + (rNo.testo.match(/^.*(redist|Sfoglia).*$/mi) || [""])[0]);
const aiuto = installa(["--aiuto"]);
esito("A7. --aiuto no longer lists --addin", aiuto.uscita === 0 && !aiuto.testo.includes("--addin"),
  (aiuto.testo.match(/^.*--addin.*$/m) || ["exit " + aiuto.uscita])[0]);
esito("A7. --aiuto lists the new options",
  ["--log", "--keep-client", "--weldment-profiles", "--verify", "--json"].every(o => aiuto.testo.includes(o)),
  ["--log", "--keep-client", "--weldment-profiles", "--verify", "--json"].filter(o => !aiuto.testo.includes(o)).join(", ") || "all");
// SW_INSTALL_PATH is the root (entry H10); the installer looked only at SW_REDIST.
const fakeSw = path.join(BANCO, "fake-sw");
fs.mkdirSync(path.join(fakeSw, "api", "redist"), { recursive: true });
fs.writeFileSync(path.join(fakeSw, "api", "redist", "SolidWorks.Interop.sldworks.dll"), "");
const rRoot = installa(["--silenzioso", "--senza-client", "--cartella", path.join(BANCO, "installed-root")],
  { env: { SW_INSTALL_PATH: fakeSw, SW_REDIST: "" } });
esito("A7. the installer finds SOLIDWORKS through SW_INSTALL_PATH (the root)",
  rRoot.testo.includes(path.join(fakeSw, "api", "redist")),
  (rRoot.testo.match(/^.*SOLIDWORKS install.*$/mi) || rRoot.testo.match(/^\[..?\].*SOLIDWORKS.*$/m) || [""])[0]);

// ── B2 — a real console, a log, and an exit code without pipes ───────────
console.log("\n=== H19 B2 - log and launch without pipes");
const logText = read(logBad);
esito("B2. --log writes the run to a file", !!logText && logText.includes("Paste this into"),
  logText ? logText.split(/\r?\n/).slice(-3).join(" | ") : "no log file");
// The installer is a Windows (GUI) program: launched from a shell without a
// pipe, the shell may not wait and the exit code may be lost. These are the
// two forms INSTALL-FOR-AI will give, launched here exactly that way.
const cfgBad2 = path.join(BANCO, "malformed2.json");
fs.writeFileSync(cfgBad2, "{ not json\n");
const argsBad2 = "--silenzioso --cartella \"" + path.join(BANCO, "installed-bad2") + "\" --config \"" + cfgBad2 + "\"";
const ps = spawnSync("powershell", ["-NoProfile", "-Command",
  "$p = Start-Process -FilePath '" + INSTALLA + "' -ArgumentList '" + argsBad2.replace(/'/g, "''") +
  "' -Wait -PassThru -NoNewWindow; exit $p.ExitCode"], { env: ENV, encoding: "utf8", timeout: 120000 });
esito("B2. Start-Process -Wait -PassThru -NoNewWindow reports exit 3", ps.status === 3, "exit " + ps.status);
const bat = path.join(BANCO, "launch.bat");
fs.writeFileSync(bat, "@echo off\r\nstart \"\" /wait \"" + INSTALLA + "\" " + argsBad2 + "\r\nexit /b %ERRORLEVEL%\r\n");
const cmd = spawnSync("cmd", ["/d", "/c", bat], { env: ENV, encoding: "utf8", timeout: 120000 });
esito("B2. cmd: start \"\" /wait in a batch file reports exit 3", cmd.status === 3, "exit " + cmd.status);

// ── B3 — the bridge's self-test answers with a code ──────────────────────
console.log("\n=== H19 B3 - the self-test code");
const bridgeExe = path.join(cartella, "oce-solid-agent-mcp.exe");
if (!ATTACH) {
  const pr = spawnSync(bridgeExe, ["--prova"], { env: ENV, encoding: "utf8", timeout: 120000 });
  esito("B3. --prova with OCE_NO_ATTACH exits 5 (attach skipped on request)", pr.status === 5,
    "exit " + pr.status + "\n" + String(pr.stderr || "").split(/\r?\n/).slice(0, 2).join("\n"));
} else console.log("  (skipped with --attach: the codes 0, 3 and 4 need SOLIDWORKS open, closed, absent)");

// ── B4 — the installer verifies itself ───────────────────────────────────
console.log("\n=== H19 B4 - --verify");
// The number of tools is counted here independently, with a handshake of
// our own, and compared with what --verify says.
function handshake(exe) {
  const input =
    JSON.stringify({ jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2024-11-05", capabilities: {}, clientInfo: { name: "prova-installatore", version: "1" } } }) + "\n" +
    JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" }) + "\n" +
    JSON.stringify({ jsonrpc: "2.0", id: 2, method: "tools/list", params: {} }) + "\n";
  const r = spawnSync(exe, [], { input, env: ENV, encoding: "utf8", timeout: 120000, maxBuffer: 64 * 1024 * 1024 });
  for (const line of String(r.stdout || "").split(/\r?\n/)) {
    try { const m = JSON.parse(line); if (m.id === 2 && m.result) return m.result.tools.length; } catch (e) { }
  }
  return -1;
}
const tools = handshake(bridgeExe);
const rv = installa(["--verify", "--json", "--cartella", cartella, "--config", cfg], { timeout: 90000 });
let vj = null;
try { vj = JSON.parse(rv.testo.trim().split(/\r?\n/).filter(l => l.startsWith("{")).pop()); } catch (e) { }
esito("B4. --verify --json prints one JSON line", !!vj, rv.testo.slice(0, 300) || "exit " + rv.uscita);
if (vj) {
  esito("B4. it lists every installed file as present",
    Array.isArray(vj.files) && vj.files.length >= 6 && vj.files.every(f => f.present),
    (vj.files || []).filter(f => !f.present).map(f => f.name).join(", ") || "all present");
  esito("B4. it says the bridge version, and it is VERSION.txt", vj.bridgeVersion === versione,
    vj.bridgeVersion + " vs " + versione);
  const cl = (vj.clients || [])[0] || {};
  esito("B4. the client entry points at the installed exe", cl.pointsToInstalled === true,
    cl.file + " -> " + cl.command);
  esito("B4. the MCP handshake counts the same tools as an independent handshake",
    vj.handshake && vj.handshake.ok === true && vj.handshake.tools === tools && tools > 0,
    "verify " + (vj.handshake && vj.handshake.tools) + ", independent " + tools);
  esito("B4. it reports the self-test code (5 here, attach skipped)",
    vj.selfTest && vj.selfTest.code === (ATTACH ? vj.selfTest.code : 5), JSON.stringify(vj.selfTest));
  esito("B4. exit " + (ATTACH ? "0 (all green, SOLIDWORKS attached)" : "2 (nothing red; the self-test did not attach)"),
    rv.uscita === (ATTACH ? 0 : 2) && vj.exit === rv.uscita, "exit " + rv.uscita + ", json exit " + vj.exit);
}
const rvh = installa(["--verify", "--cartella", cartella, "--config", cfg], { timeout: 90000 });
esito("B4. without --json it prints readable lines", /^\[ok\] /m.test(rvh.testo) && !/^\{/m.test(rvh.testo),
  rvh.testo.split(/\r?\n/).slice(0, 3).join(" | "));
const rve = installa(["--verify", "--json", "--cartella", path.join(BANCO, "nothing-here"), "--config", cfg], { timeout: 90000 });
esito("B4. --verify on a folder with nothing installed exits 1", rve.uscita === 1, "exit " + rve.uscita);

// ── A6 — the weldment profiles reach the bridge ──────────────────────────
console.log("\n=== H19 A6 - --weldment-profiles");
const profilesDir = path.join(BANCO, "weldment profiles");
fs.mkdirSync(path.join(profilesDir, "iso"), { recursive: true });
fs.writeFileSync(path.join(profilesDir, "iso", "square tube.sldlfp"), "");
const cfgA6 = path.join(BANCO, "a6.json");
fs.copyFileSync(MODELLO, cfgA6);
const rA6 = installa(["--silenzioso", "--cartella", path.join(BANCO, "installed-a6"), "--config", cfgA6, "--weldment-profiles", profilesDir]);
const a6 = json(cfgA6);
const sw6 = a6 && a6.mcpServers && a6.mcpServers.solidworks;
esito("A6. PROFILI_SALDATURA is written into the entry's env",
  rA6.uscita === 0 && !!sw6 && !!sw6.env && sw6.env.PROFILI_SALDATURA === profilesDir,
  "exit " + rA6.uscita + ", env " + JSON.stringify(sw6 && sw6.env));
esito("A6. the other servers are untouched",
  !!a6 && uguale(a6.mcpServers.filesystem, prima.mcpServers.filesystem) && uguale(a6.mcpServers.github, prima.mcpServers.github), null);
const cfgA6b = path.join(BANCO, "a6b.json");
fs.copyFileSync(MODELLO, cfgA6b);
const byteA6b = fs.readFileSync(cfgA6b);
const rA6b = installa(["--silenzioso", "--cartella", path.join(BANCO, "installed-a6b"), "--config", cfgA6b, "--weldment-profiles", path.join(BANCO, "no-such-folder")]);
esito("A6. a weldment profiles folder that does not exist: exit 1, nothing installed, config untouched",
  rA6b.uscita === 1 && !fs.existsSync(path.join(BANCO, "installed-a6b")) && Buffer.compare(byteA6b, fs.readFileSync(cfgA6b)) === 0,
  "exit " + rA6b.uscita);

// ── A5 — uninstall is coherent ───────────────────────────────────────────
console.log("\n=== H19 A5 - uninstall");
const dirA5 = path.join(BANCO, "installed-a5");
const cfgA5 = path.join(BANCO, "a5.json");
fs.copyFileSync(MODELLO, cfgA5);
installa(["--silenzioso", "--cartella", dirA5, "--config", cfgA5]);
fs.writeFileSync(path.join(dirA5, "domande-senza-risposta.jsonl"), "{\"name\":\"x\"}\n");
const rA5 = installa(["--disinstalla", "--cartella", dirA5, "--config", cfgA5]);
const a5 = json(cfgA5);
esito("A5. uninstall removes the client entry (no --keep-client)",
  rA5.uscita === 0 && !!a5 && !(a5.mcpServers || {}).solidworks,
  "exit " + rA5.uscita + ", servers " + Object.keys((a5 || {}).mcpServers || {}).join(", "));
esito("A5. ...and leaves the other servers", !!a5 && Object.keys(a5.mcpServers).sort().join(",") === "filesystem,github", null);
esito("A5. it removes the log the bridge writes beside itself (.jsonl), and then the folder",
  !fs.existsSync(dirA5), fs.existsSync(dirA5) ? "left: " + fs.readdirSync(dirA5).join(", ") : null);

const dirK = path.join(BANCO, "installed-keep");
const cfgK = path.join(BANCO, "keep.json");
fs.copyFileSync(MODELLO, cfgK);
installa(["--silenzioso", "--cartella", dirK, "--config", cfgK]);
const rK = installa(["--disinstalla", "--cartella", dirK, "--config", cfgK, "--keep-client"]);
const k = json(cfgK);
esito("A5. --keep-client keeps the entry", rK.uscita === 0 && !!k && !!(k.mcpServers || {}).solidworks,
  "exit " + rK.uscita);

const cfgE = path.join(BANCO, "elsewhere.json");
const elsewhereCfg = { mcpServers: { solidworks: { command: "C:\\Somewhere\\Else\\oce-solid-agent-mcp.exe" } } };
fs.writeFileSync(cfgE, JSON.stringify(elsewhereCfg, null, 2));
const rE = installa(["--disinstalla", "--cartella", path.join(BANCO, "installed-elsewhere"), "--config", cfgE]);
esito("A5. an entry that points at another installation is left alone",
  rE.uscita === 0 && uguale(json(cfgE), elsewhereCfg), "exit " + rE.uscita + "\n" + rE.testo.slice(-200));

const dirL = path.join(BANCO, "installed-locked");
installa(["--silenzioso", "--senza-client", "--cartella", dirL]);
const heldFile = path.join(dirL, "catalogo.json");
const lockedReady = path.join(BANCO, "locked.ready");
const holder = require("child_process").spawn("powershell", ["-NoProfile", "-Command",
  "$f=[System.IO.File]::Open('" + heldFile + "','Open','ReadWrite','None'); " +
  "Set-Content -Path '" + lockedReady + "' -Value ok; Start-Sleep -Seconds 60; $f.Close()"], { stdio: "ignore" });
const waitMs = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
for (let i = 0; i < 100 && !fs.existsSync(lockedReady); i++) waitMs(100);
const rL = installa(["--disinstalla", "--cartella", dirL, "--keep-client"]);
holder.kill();
waitMs(500);
esito("A5. a file that cannot be removed: exit 1, and it says which",
  rL.uscita === 1 && rL.testo.includes("could not be removed") && rL.testo.includes("catalogo.json"),
  "exit " + rL.uscita + "\n" + (rL.testo.match(/^.*could not be removed.*$/m) || [rL.testo.slice(-200)])[0]);

// ── B5 — Claude Code through its own CLI ─────────────────────────────────
//
// Never against the real Claude Code: a fake `claude.cmd` records what it is
// asked, and the installer's own code is called by reflection from a child
// PowerShell, so no real client configuration is read or written.
console.log("\n=== H19 B5 - claude mcp add");
const fake = path.join(BANCO, "fake-claude");
fs.mkdirSync(fake);
const argsFile = path.join(fake, "args.txt");
fs.writeFileSync(path.join(fake, "claude.cmd"),
  "@echo off\r\necho %*>> \"%~dp0args.txt\"\r\nexit /b %FAKE_CLAUDE_EXIT%\r\n");
function cliCall(exitCode) {
  try { fs.unlinkSync(argsFile); } catch (e) { }
  const script =
    "$ErrorActionPreference='Stop';" +
    // From the bytes, not LoadFrom: on the network drive the repository
    // lives on, LoadFrom is refused (0x80131515), and these cases were red
    // there for a reason that has nothing to do with the installer.
    "$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes('" + INSTALLA + "'));" +
    "$t=$a.GetType('Oce.Solid.Agent.Installer.ClaudeCli');" +
    "if(-not $t){'NO-TYPE';exit 9};" +
    "$bf=[Reflection.BindingFlags]'NonPublic,Public,Static';" +
    "$found=$t.GetMethod('Find',$bf).Invoke($null,@());" +
    "'FOUND='+$found;" +
    "$d=New-Object 'System.Collections.Generic.Dictionary[string,string]';" +
    "$d['PROFILI_SALDATURA']='C:\\Weld Profiles';" +
    // a typed object[] takes the dictionary out of its PowerShell wrapper
    "$p=New-Object object[] 4;$p[0]=[string]$found;$p[1]='C:\\Some Folder\\oce-solid-agent-mcp.exe';$p[2]=$d.PSObject.BaseObject;$p[3]=$null;" +
    "$ok=$t.GetMethod('Register',$bf).Invoke($null,$p);" +
    "'OK='+$ok";
  const e = Object.assign({}, ENV, { PATH: fake + ";" + ENV.PATH, FAKE_CLAUDE_EXIT: String(exitCode) });
  const r = spawnSync("powershell", ["-NoProfile", "-Command", script], { env: e, encoding: "utf8", timeout: 120000 });
  return { out: (r.stdout || "") + (r.stderr || ""), args: read(argsFile) || "" };
}
const c0 = cliCall(0);
esito("B5. the installer finds claude on PATH", c0.out.includes("FOUND=" + path.join(fake, "claude.cmd")),
  c0.out.split(/\r?\n/).slice(0, 2).join(" | "));
const cliLines = c0.args.trim().split(/\r?\n/);
esito("B5. it removes the old user-scope entry, then adds it with claude mcp add --scope user",
  cliLines.length === 2 && cliLines[0].trim() === "mcp remove --scope user solidworks" &&
  cliLines[1].startsWith("mcp add --scope user solidworks -e \"PROFILI_SALDATURA=C:\\Weld Profiles\" -- \"C:\\Some Folder\\oce-solid-agent-mcp.exe\""),
  c0.args.trim() || c0.out.slice(-200));
esito("B5. a successful CLI is reported as success", /OK=True/.test(c0.out), c0.out.slice(-120));
const c1 = cliCall(1);
esito("B5. a failing CLI is reported as failure (the installer then writes the file itself)",
  /OK=False/.test(c1.out), c1.out.slice(-120));

// ═════════════════════════════════════════════════════════════════════════
//  THE H19 REVIEW. Every case below was run red on the installer of bda8946
//  (the L1+L2 commit) before the fixes; the ones that need a profile with no
//  client refuse to install when the installer would look at the real one.
// ═════════════════════════════════════════════════════════════════════════

// Wire, the way the installer calls it for Claude Code, with the fake claude:
// the entry's own variables reach `claude mcp add`, and a backup is made first.
console.log("\n=== H19 review - Claude Code through claude: backup and env kept");
const fakeHome = path.join(BANCO, "fake-claude-home");
fs.mkdirSync(fakeHome);
const fakeClaudeJson = path.join(fakeHome, ".claude.json");
fs.writeFileSync(fakeClaudeJson, JSON.stringify({ mcpServers: { solidworks: {
  command: "C:\\Some Folder\\oce-solid-agent-mcp.exe", env: { RISCONTRO_RADICE: "C:\\Work" } } } }, null, 2));
try { fs.unlinkSync(argsFile); } catch (e) { }
const wireScript =
  "$ErrorActionPreference='Stop';" +
  "$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes('" + INSTALLA + "'));" +
  "$bf=[Reflection.BindingFlags]'NonPublic,Public,Static,Instance';" +
  "$ct=$a.GetType('Oce.Solid.Agent.Installer.Client');$c=[Activator]::CreateInstance($ct,$true);" +
  "$ct.GetField('Nome',$bf).SetValue($c,'Claude Code');$ct.GetField('File',$bf).SetValue($c,'" + fakeClaudeJson + "');" +
  "$ct.GetField('ClaudeCode',$bf).SetValue($c,$true);" +
  "$tell=[Action[string]]{ param($s) [Console]::WriteLine('TELL '+$s) };" +
  "$p=New-Object object[] 6;$p[0]=$c;$p[1]='C:\\Some Folder\\oce-solid-agent-mcp.exe';$p[2]=$null;$p[3]=$null;$p[4]=$null;$p[5]=$tell;" +
  "$a.GetType('Oce.Solid.Agent.Installer.Programma').GetMethod('Wire',$bf).Invoke($null,$p);'WIRED'";
const wr = spawnSync("powershell", ["-NoProfile", "-Command", wireScript],
  { env: Object.assign({}, ENV, { PATH: fake + ";" + ENV.PATH, FAKE_CLAUDE_EXIT: "0" }), encoding: "utf8", timeout: 120000 });
const wireArgs = read(argsFile) || "";
esito("review. claude mcp add receives the variables already in the entry (RISCONTRO_RADICE)",
  /mcp add --scope user solidworks .*-e "RISCONTRO_RADICE=C:\\Work"/.test(wireArgs),
  wireArgs.trim() || ((wr.stdout || "") + (wr.stderr || "")).slice(-300));
esito("review. a backup of .claude.json is made before claude rewrites it",
  fs.readdirSync(fakeHome).some(f => /^\.claude\.json\.prima-di-oce-solid-agent-.*\.bak$/.test(f)),
  fs.readdirSync(fakeHome).join(", "));

// A reinstall keeps what is already in the entry's env, by the file route.
console.log("\n=== H19 review - a reinstall keeps the entry's own env");
const keepDir = path.join(BANCO, "installed-keepenv");
const mineProfiles = path.join(BANCO, "my profiles");
fs.mkdirSync(mineProfiles);
const cfgKeep = path.join(BANCO, "keepenv.json");
const keepCfg = { mcpServers: {
  other: { command: "C:\\Other\\server.exe", env: { TOKEN_FILE: "C:\\Other\\t.txt" } },
  solidworks: { command: path.join(keepDir, "oce-solid-agent-mcp.exe"),
                env: { RISCONTRO_RADICE: "C:\\Work", PROFILI_SALDATURA: mineProfiles } } } };
fs.writeFileSync(cfgKeep, JSON.stringify(keepCfg, null, 2));
const rKeep = installa(["--silenzioso", "--cartella", keepDir, "--config", cfgKeep]);
const ke = json(cfgKeep);
const ksw = (ke && ke.mcpServers && ke.mcpServers.solidworks) || {};
esito("review. a reinstall keeps a variable already in the entry (RISCONTRO_RADICE)",
  rKeep.uscita === 0 && !!ksw.env && ksw.env.RISCONTRO_RADICE === "C:\\Work",
  "exit " + rKeep.uscita + ", env " + JSON.stringify(ksw.env));
esito("review. ...and a PROFILI_SALDATURA chosen by hand, and says it kept it",
  !!ksw.env && ksw.env.PROFILI_SALDATURA === mineProfiles && /kept from the existing client entry/.test(rKeep.testo),
  (rKeep.testo.match(/^.*weldment profiles.*$/m) || ["no weldment line"])[0]);
esito("review. the other server and its env are untouched",
  !!ke && uguale(ke.mcpServers.other, keepCfg.mcpServers.other), null);

// A profile with no client at all. The environment points the installer at an
// empty profile; before installing anything, a read-only --verify says where
// it would look, and if that is not the empty profile the install is NOT run
// (it would write the real configuration of whoever runs this).
console.log("\n=== H19 review - no MCP client on the machine");
const noClaudePath = (ENV.PATH || "").split(";").filter(d => {
  try { return !["claude.exe", "claude.cmd", "claude.bat"].some(n => fs.existsSync(path.join(d.trim().replace(/"/g, ""), n))); }
  catch (e) { return true; }
}).join(";");
function profileEnv(dir) {
  fs.mkdirSync(path.join(dir, "AppData", "Roaming"), { recursive: true });
  return { env: { USERPROFILE: dir, APPDATA: path.join(dir, "AppData", "Roaming"), CLAUDE_CONFIG_DIR: "", PATH: noClaudePath } };
}
function looksOnlyIn(dir, opts) {
  const r = installa(["--verify", "--json", "--cartella", path.join(BANCO, "nothing-here")], Object.assign({ timeout: 90000 }, opts));
  let j = null;
  try { j = JSON.parse(r.testo.trim().split(/\r?\n/).filter(l => l.startsWith("{")).pop()); } catch (e) { }
  const outside = j ? (j.clients || []).filter(c => !String(c.file || "").toLowerCase().startsWith(dir.toLowerCase())) : null;
  return { ok: !!j && outside.length === 0, detail: j ? outside.map(c => c.name + " -> " + c.file).join("; ") : r.testo.slice(-200) };
}
const emptyProfile = path.join(BANCO, "profile-without-clients");
const emptyOpts = profileEnv(emptyProfile);
const where = looksOnlyIn(emptyProfile, emptyOpts);
esito("review. in a profile with no client the installer finds none (Claude Code is not 'installed' everywhere)",
  where.ok, where.ok ? null : "it would use: " + where.detail);
if (where.ok) {
  const rNc = installa(["--silenzioso", "--cartella", path.join(BANCO, "installed-noclient")], emptyOpts);
  esito("review. no MCP client: exit 3, it says so and prints the block to paste",
    rNc.uscita === 3 && rNc.testo.includes("no MCP client found") && rNc.testo.includes("Paste this into"),
    "exit " + rNc.uscita + "\n" + rNc.testo.slice(-300));
  esito("review. ...and it creates no .claude.json nobody reads", !fs.existsSync(path.join(emptyProfile, ".claude.json")),
    fs.readdirSync(emptyProfile).join(", "));

  const ccProfile = path.join(BANCO, "profile-with-claude-code");
  const ccOpts = profileEnv(ccProfile);
  fs.mkdirSync(path.join(ccProfile, ".claude"));
  const ccDir = path.join(BANCO, "installed-cc");
  const rCc = installa(["--silenzioso", "--cartella", ccDir], ccOpts);
  const cc = json(path.join(ccProfile, ".claude.json"));
  esito("review. a profile with a .claude folder: Claude Code is found and wired through its file (no claude on PATH)",
    rCc.uscita === 0 && !!cc && !!cc.mcpServers && !!cc.mcpServers.solidworks &&
    String(cc.mcpServers.solidworks.command).toLowerCase() === path.join(ccDir, "oce-solid-agent-mcp.exe").toLowerCase(),
    "exit " + rCc.uscita + "\n" + rCc.testo.slice(-200));
} else console.log("  (the install cases of this block were NOT run: the installer would touch the real profile)");

// --verify on a profile where one client is wired and the other's config is
// malformed: not all green behind an install that exited 3 for it.
const badProfile = path.join(BANCO, "profile-with-a-bad-config");
const badOpts = profileEnv(badProfile);
fs.mkdirSync(path.join(badProfile, "AppData", "Roaming", "Claude"), { recursive: true });
fs.writeFileSync(path.join(badProfile, "AppData", "Roaming", "Claude", "claude_desktop_config.json"), "{ not json\n");
fs.writeFileSync(path.join(badProfile, ".claude.json"), JSON.stringify({ mcpServers: { solidworks: {
  command: path.join(keepDir, "oce-solid-agent-mcp.exe") } } }, null, 2));
const rvBad = installa(["--verify", "--json", "--cartella", keepDir], Object.assign({ timeout: 90000 }, badOpts));
let vBad = null;
try { vBad = JSON.parse(rvBad.testo.trim().split(/\r?\n/).filter(l => l.startsWith("{")).pop()); } catch (e) { }
const clientsCheck = vBad && (vBad.checks || []).find(c => c.name === "clients");
esito("review. --verify warns about a client config that cannot be read, even when another client is wired",
  !!clientsCheck && clientsCheck.status === "warn" && /cannot be read/.test(clientsCheck.detail),
  clientsCheck ? clientsCheck.status + ": " + clientsCheck.detail : rvBad.testo.slice(-200));

// The elevated case: "not running" while a SLDWORKS process runs in this
// session is 6, not 3. A copy of node named SLDWORKS.exe stands in for it. Only
// when no real SOLIDWORKS runs: the bridge is started without OCE_NO_ATTACH,
// and with no SOLIDWORKS to answer, attaching touches nothing.
console.log("\n=== H19 review - self-test 6, a SLDWORKS process that does not answer");
const realSw = spawnSync("tasklist", ["/FI", "IMAGENAME eq SLDWORKS.exe", "/NH"], { encoding: "utf8" });
const swRuns = /SLDWORKS\.exe/i.test(String(realSw.stdout || ""));
if (ATTACH || swRuns) {
  console.log("  (NOT run: " + (swRuns ? "a real SOLIDWORKS is running" : "--attach") +
              ". With SOLIDWORKS closed: self-test 3, and 6 with a fake SLDWORKS process)");
} else {
  const envAttach = Object.assign({}, ENV); delete envAttach.OCE_NO_ATTACH;
  const p3 = spawnSync(bridgeExe, ["--prova"], { env: envAttach, encoding: "utf8", timeout: 120000 });
  if (p3.status === 4) console.log("  (NOT run: self-test 4, SOLIDWORKS is not installed on this machine)");
  else {
    esito("review. SOLIDWORKS closed: --prova exits 3", p3.status === 3, "exit " + p3.status);
    const fakeSw = path.join(BANCO, "fake-sldworks");
    fs.mkdirSync(fakeSw);
    const fakeExe = path.join(fakeSw, "SLDWORKS.exe");
    fs.copyFileSync(process.execPath, fakeExe);
    const sw = require("child_process").spawn(fakeExe, ["-e", "setTimeout(()=>{},60000)"], { stdio: "ignore" });
    aspetta(1500);
    const p6 = spawnSync(bridgeExe, ["--prova"], { env: envAttach, encoding: "utf8", timeout: 120000 });
    sw.kill();
    esito("review. a SLDWORKS process that does not answer: --prova exits 6 and says why", p6.status === 6 &&
      /administrator/.test(String(p6.stderr || "")), "exit " + p6.status + "\n" + String(p6.stderr || "").split(/\r?\n/).slice(-2).join("\n"));
  }
}

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
  esito("4. exit code 3: files installed, the client not wired (H19 B1)", r4.uscita === 3,
    "exit " + r4.uscita);
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
// H19 A5: uninstall now removes the client entry too; --config keeps this
// run on the temporary file, away from the real clients of whoever runs it.
const r3 = installa(["--disinstalla", "--cartella", cartella, "--config", cfg]);
const after3 = json(cfg);
esito("3. exit 0, and the entry is gone from the client config (H19 A5)",
  r3.uscita === 0 && !!after3 && !(after3.mcpServers || {}).solidworks,
  "exit " + r3.uscita + ", servers " + Object.keys((after3 || {}).mcpServers || {}).join(", "));

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
if (!ATTACH) {
  console.log("\n  Run without touching SOLIDWORKS (OCE_NO_ATTACH). With the bench stopped, the owner runs");
  console.log("    node Controlli\\prova-installatore.js --attach");
  console.log("  which is the only run where --verify can exit 0. The self-test codes 3 (SOLIDWORKS closed)");
  console.log("  and 4 (not installed or not registered) need SOLIDWORKS closed, or a machine without it.");
}
console.log("\n  banco lasciato in " + BANCO + " se serve guardarci dentro");
process.exit(falliti.length ? 1 : 0);
