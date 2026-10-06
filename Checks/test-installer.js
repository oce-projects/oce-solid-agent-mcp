// The installer checks that can be done on any machine.
//
//     node Checks\test-installer.js            (SOLIDWORKS not touched)
//     node Checks\test-installer.js --attach   (the real session: bench stopped)
//
// Exits 0 if they all pass, 1 if not.
//
// It works in a temporary folder: THE REAL CONFIGURATION OF WHOEVER IS TESTING MUST NOT
// BE TOUCHED, and we do not rely on remembering it.
//
// THERE ARE SIX SCENARIOS, AND THIS ONE RUNS THREE AND A HALF.
//
//   | # | check                                   | here       |
//   |---|-----------------------------------------|------------|
//   | 1 | installation on a clean machine         | no: VM with SOLIDWORKS and without the bridge |
//   | 2 | installation over a previous one        | **yes**    |
//   | 3 | uninstallation                          | **yes**    |
//   | 4 | client open during the installation     | **half**: the file held by another process yes; the real Claude Desktop no |
//   | 5 | SOLIDWORKS absent                       | no: needs a machine without SOLIDWORKS |
//   | 6 | config with other MCP servers already there | **yes**  |
//
// The other three are done by hand on a machine that is not here. They must be
// listed as NOT RUN wherever their outcome is reported: *a check not
// done is written down, it is not declared passed.*
//
// CASE 6 IS THE MOST IMPORTANT, and it can all be done here: «are there already
// other MCP servers, do they stay intact?». It produces no symptom at once — the
// file looks well written until somebody reopens the client the next day
// and finds gone the server they used to work.
//
// The STRUCTURE is compared, not the text: if the test JSON has different spacing
// from the one the installer produces, a diff of the whole files
// would report a failure that is not there.
const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const os = require("os");

const HERE = __dirname;
const ROOT = path.dirname(HERE);
const INSTALL = process.env.INSTALLA || path.join(ROOT, "dist", "Installer.exe");
const MODEL = path.join(HERE, "config-with-other-servers.json");

// We work in a temporary folder: the REAL configuration of whoever is
// testing is not touched, and we do not rely on remembering it.
const BENCH = fs.mkdtempSync(path.join(os.tmpdir(), "prova-installatore-"));

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

function install(args, opts) {
  opts = opts || {};
  const r = spawnSync(INSTALL, args, {
    encoding: "utf8", maxBuffer: 32 * 1024 * 1024,
    env: Object.assign({}, ENV, opts.env || {}),
    // An old installer that does not know an option opens its window and
    // waits for a click: the timeout turns that into a red line instead of
    // a run that never ends.
    timeout: opts.timeout || 120000,
  });
  return { uscita: r.status, testo: (r.stdout || "") + (r.stderr || "") };
}

const outcomes = [];
function outcome(testCase, passes, detail) {
  outcomes.push({ caso: testCase, passa: passes });
  console.log("  " + (passes ? "PASS" : "FAIL") + "  " + testCase);
  if (detail) console.log("        " + String(detail).replace(/\n/g, "\n        "));
}

if (!fs.existsSync(INSTALL)) {
  console.error("cannot find " + INSTALL + " — build first with build.ps1");
  process.exit(2);
}
console.log("installatore: " + INSTALL);
console.log("banco:        " + BENCH);

const equal = (a, b) => JSON.stringify(a) === JSON.stringify(b);

// ── CASE 6 — the other servers stay ──────────────────────────────────────
console.log("\n=== CASE 6 - with two MCP servers already present");
const cfg = path.join(BENCH, "claude_desktop_config.json");
fs.copyFileSync(MODEL, cfg);
const before = JSON.parse(fs.readFileSync(cfg, "utf8"));
const folder = path.join(BENCH, "installato");

const r6 = install(["--silenzioso", "--cartella", folder, "--config", cfg]);
// H19 B1: 0 means installed AND the client wired.
outcome("6. exit code 0: installed and the client wired (H19 B1)", r6.uscita === 0,
  "exit " + r6.uscita + "\n" + r6.testo.slice(-400));

let after = null;
try { after = JSON.parse(fs.readFileSync(cfg, "utf8")); } catch (e) { }
outcome("6. the config stays valid JSON", !!after, after ? null : "cannot be read back");

if (after) {
  for (const itemName of ["filesystem", "github"]) {
    const d = (after.mcpServers || {})[itemName];
    outcome("6. '" + itemName + "' is still identical",
      !!d && equal(before.mcpServers[itemName], d),
      d ? null : "SPARITO");
  }
  outcome("6. 'solidworks' appeared", !!(after.mcpServers || {}).solidworks, null);
  outcome("6. nothing else appeared",
    Object.keys(after.mcpServers || {}).sort().join(",") === "filesystem,github,solidworks",
    Object.keys(after.mcpServers || {}).sort().join(", "));
  outcome("6. the client's settings outside mcpServers are intact",
    equal(before.altreImpostazioniDelClient, after.altreImpostazioniDelClient), null);
}

// ── CASE 2 — installing over a previous one ──────────────────────────────
console.log("\n=== CASE 2 - installation over a previous one");
const r2 = install(["--silenzioso", "--cartella", folder, "--config", cfg]);
let after2 = null;
try { after2 = JSON.parse(fs.readFileSync(cfg, "utf8")); } catch (e) { }

outcome("2. the second installation does not fail", r2.uscita === 0,
  r2.uscita === 0 ? null : "uscita " + r2.uscita + "\n" + r2.testo.slice(-400));
outcome("2. the 'solidworks' entry is updated, not duplicated",
  !!after2 && Object.keys(after2.mcpServers || {}).sort().join(",") === "filesystem,github,solidworks",
  after2 ? Object.keys(after2.mcpServers || {}).sort().join(", ") : "config unreadable");

const bak = fs.readdirSync(BENCH).filter(f => /\.bak$/i.test(f));
outcome("2. there is a backup copy of the config", bak.length > 0, bak.join("\n"));

// H15: whoever reinstalls knows whether it is updating or putting back the same one. The
// expected version is read from VERSION.txt, not written here: it would change at
// every release and the case would turn red for the wrong reason.
const version = fs.readFileSync(path.join(ROOT, "VERSION.txt"), "utf8").trim();
outcome("2. it says it is reinstalling the same version, and which one",
  r2.testo.includes("reinstalling " + version),
  (r2.testo.match(/^.*(installing|updating).*$/mi) || ["no version line"])[0].trim());

// H15: the backup copies are pruned. Before, they were never removed: ten
// installations, ten .bak in %APPDATA%. The name carries the seconds, so between
// one run and the next we wait a second, otherwise two copies would have the same
// name and the count would prove nothing.
const wait = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
for (let i = 0; i < 4; i++) {
  wait(1100);
  install(["--silenzioso", "--cartella", folder, "--config", cfg]);
}
const bakAfter = fs.readdirSync(BENCH).filter(f => /\.prima-di-oce-solid-agent-.*\.bak$/i.test(f));
outcome("2. after six installations the backups are at most three",
  bakAfter.length > 0 && bakAfter.length <= 3, bakAfter.length + " copie: " + bakAfter.join(", "));

// ═════════════════════════════════════════════════════════════════════════
//  ENTRY H19, LOTS L1 AND L2. Every case below was run red on the installer
//  of 47d0f8f (c7ab519 plus OCE_NO_ATTACH only) before it was run green.
// ═════════════════════════════════════════════════════════════════════════

const read = f => { try { return fs.readFileSync(f, "utf8"); } catch (e) { return null; } };
const json = f => { try { return JSON.parse(fs.readFileSync(f, "utf8")); } catch (e) { return null; } };
// The line before the block to paste is looked for as a literal, where the
// inventory of consumed strings (Checks/language/consumed-strings.json) can
// find it: installer-paste-header.

// ── B1 — the exit code tells the truth ───────────────────────────────────
console.log("\n=== H19 B1 - exit codes");
const cfgBad = path.join(BENCH, "malformed.json");
fs.writeFileSync(cfgBad, "{ \"mcpServers\": { this is not JSON\n");
const byteBad = fs.readFileSync(cfgBad);
const logBad = path.join(BENCH, "malformed.log");
const rBad = install(["--silenzioso", "--cartella", path.join(BENCH, "installed-bad"), "--config", cfgBad, "--log", logBad]);
outcome("B1. a malformed client config: exit 3 (files installed, no client wired)", rBad.uscita === 3,
  "exit " + rBad.uscita + "\n" + rBad.testo.slice(-300));
outcome("B1. the malformed config is left byte for byte as it was",
  Buffer.compare(byteBad, fs.readFileSync(cfgBad)) === 0, null);
outcome("B1. exit 3 prints the block to paste by hand",
  rBad.testo.includes("Paste this into") && /"solidworks"/.test(rBad.testo),
  (rBad.testo.match(/^.*Paste.*$/m) || ["no paste line"])[0]);

const rNone = install(["--silenzioso", "--senza-client", "--cartella", path.join(BENCH, "installed-none")]);
outcome("B1. --senza-client: exit 0 (no client asked for)", rNone.uscita === 0,
  "exit " + rNone.uscita + "\n" + rNone.testo.slice(-300));

const emptyRedist = path.join(BENCH, "empty-redist");
fs.mkdirSync(emptyRedist);
const neverInstalled = path.join(BENCH, "never-installed");
const rNo = install(["--silenzioso", "--redist", emptyRedist, "--cartella", neverInstalled, "--config", path.join(BENCH, "never.json")]);
outcome("B1. a prerequisite fails: exit 1 and nothing installed",
  rNo.uscita === 1 && !fs.existsSync(neverInstalled) && !fs.existsSync(path.join(BENCH, "never.json")),
  "exit " + rNo.uscita + ", folder " + (fs.existsSync(neverInstalled) ? "created" : "absent"));

// ── A7 — stale texts ─────────────────────────────────────────────────────
console.log("\n=== H19 A7 - stale texts");
outcome("A7. a missing SOLIDWORKS in silent mode names --redist, not a Browse button",
  !/Sfoglia/.test(rNo.testo) && rNo.testo.includes("--redist"),
  (rNo.testo.match(/^.*SOLIDWORKS.*$/m) || [""])[0] + "\n" + (rNo.testo.match(/^.*(redist|Sfoglia).*$/mi) || [""])[0]);
const help = install(["--aiuto"]);
outcome("A7. --aiuto no longer lists --addin", help.uscita === 0 && !help.testo.includes("--addin"),
  (help.testo.match(/^.*--addin.*$/m) || ["exit " + help.uscita])[0]);
outcome("A7. --aiuto lists the new options",
  ["--log", "--keep-client", "--weldment-profiles", "--verify", "--json"].every(o => help.testo.includes(o)),
  ["--log", "--keep-client", "--weldment-profiles", "--verify", "--json"].filter(o => !help.testo.includes(o)).join(", ") || "all");
// SW_INSTALL_PATH is the root (entry H10); the installer looked only at SW_REDIST.
const fakeSw = path.join(BENCH, "fake-sw");
fs.mkdirSync(path.join(fakeSw, "api", "redist"), { recursive: true });
fs.writeFileSync(path.join(fakeSw, "api", "redist", "SolidWorks.Interop.sldworks.dll"), "");
const rRoot = install(["--silenzioso", "--senza-client", "--cartella", path.join(BENCH, "installed-root")],
  { env: { SW_INSTALL_PATH: fakeSw, SW_REDIST: "" } });
outcome("A7. the installer finds SOLIDWORKS through SW_INSTALL_PATH (the root)",
  rRoot.testo.includes(path.join(fakeSw, "api", "redist")),
  (rRoot.testo.match(/^.*SOLIDWORKS install.*$/mi) || rRoot.testo.match(/^\[..?\].*SOLIDWORKS.*$/m) || [""])[0]);

// ── B2 — a real console, a log, and an exit code without pipes ───────────
console.log("\n=== H19 B2 - log and launch without pipes");
const logText = read(logBad);
outcome("B2. --log writes the run to a file", !!logText && logText.includes("Paste this into"),
  logText ? logText.split(/\r?\n/).slice(-3).join(" | ") : "no log file");
// The installer is a Windows (GUI) program: launched from a shell without a
// pipe, the shell may not wait and the exit code may be lost. These are the
// two forms INSTALL-FOR-AI will give, launched here exactly that way.
const cfgBad2 = path.join(BENCH, "malformed2.json");
fs.writeFileSync(cfgBad2, "{ not json\n");
const argsBad2 = "--silenzioso --cartella \"" + path.join(BENCH, "installed-bad2") + "\" --config \"" + cfgBad2 + "\"";
const ps = spawnSync("powershell", ["-NoProfile", "-Command",
  "$p = Start-Process -FilePath '" + INSTALL + "' -ArgumentList '" + argsBad2.replace(/'/g, "''") +
  "' -Wait -PassThru -NoNewWindow; exit $p.ExitCode"], { env: ENV, encoding: "utf8", timeout: 120000 });
outcome("B2. Start-Process -Wait -PassThru -NoNewWindow reports exit 3", ps.status === 3, "exit " + ps.status);
const bat = path.join(BENCH, "launch.bat");
fs.writeFileSync(bat, "@echo off\r\nstart \"\" /wait \"" + INSTALL + "\" " + argsBad2 + "\r\nexit /b %ERRORLEVEL%\r\n");
const cmd = spawnSync("cmd", ["/d", "/c", bat], { env: ENV, encoding: "utf8", timeout: 120000 });
outcome("B2. cmd: start \"\" /wait in a batch file reports exit 3", cmd.status === 3, "exit " + cmd.status);

// ── B3 — the bridge's self-test answers with a code ──────────────────────
console.log("\n=== H19 B3 - the self-test code");
const bridgeExe = path.join(folder, "oce-solid-agent-mcp.exe");
if (!ATTACH) {
  const pr = spawnSync(bridgeExe, ["--prova"], { env: ENV, encoding: "utf8", timeout: 120000 });
  outcome("B3. --prova with OCE_NO_ATTACH exits 5 (attach skipped on request)", pr.status === 5,
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
const rv = install(["--verify", "--json", "--cartella", folder, "--config", cfg], { timeout: 90000 });
let vj = null;
try { vj = JSON.parse(rv.testo.trim().split(/\r?\n/).filter(l => l.startsWith("{")).pop()); } catch (e) { }
outcome("B4. --verify --json prints one JSON line", !!vj, rv.testo.slice(0, 300) || "exit " + rv.uscita);
if (vj) {
  outcome("B4. it lists every installed file as present",
    Array.isArray(vj.files) && vj.files.length >= 6 && vj.files.every(f => f.present),
    (vj.files || []).filter(f => !f.present).map(f => f.name).join(", ") || "all present");
  outcome("B4. it says the bridge version, and it is VERSION.txt", vj.bridgeVersion === version,
    vj.bridgeVersion + " vs " + version);
  const cl = (vj.clients || [])[0] || {};
  outcome("B4. the client entry points at the installed exe", cl.pointsToInstalled === true,
    cl.file + " -> " + cl.command);
  outcome("B4. the MCP handshake counts the same tools as an independent handshake",
    vj.handshake && vj.handshake.ok === true && vj.handshake.tools === tools && tools > 0,
    "verify " + (vj.handshake && vj.handshake.tools) + ", independent " + tools);
  outcome("B4. it reports the self-test code (5 here, attach skipped)",
    vj.selfTest && vj.selfTest.code === (ATTACH ? vj.selfTest.code : 5), JSON.stringify(vj.selfTest));
  outcome("B4. exit " + (ATTACH ? "0 (all green, SOLIDWORKS attached)" : "2 (nothing red; the self-test did not attach)"),
    rv.uscita === (ATTACH ? 0 : 2) && vj.exit === rv.uscita, "exit " + rv.uscita + ", json exit " + vj.exit);
}
const rvh = install(["--verify", "--cartella", folder, "--config", cfg], { timeout: 90000 });
outcome("B4. without --json it prints readable lines", /^\[ok\] /m.test(rvh.testo) && !/^\{/m.test(rvh.testo),
  rvh.testo.split(/\r?\n/).slice(0, 3).join(" | "));
const rve = install(["--verify", "--json", "--cartella", path.join(BENCH, "nothing-here"), "--config", cfg], { timeout: 90000 });
outcome("B4. --verify on a folder with nothing installed exits 1", rve.uscita === 1, "exit " + rve.uscita);

// ── A6 — the weldment profiles reach the bridge ──────────────────────────
console.log("\n=== H19 A6 - --weldment-profiles");
const profilesDir = path.join(BENCH, "weldment profiles");
fs.mkdirSync(path.join(profilesDir, "iso"), { recursive: true });
fs.writeFileSync(path.join(profilesDir, "iso", "square tube.sldlfp"), "");
const cfgA6 = path.join(BENCH, "a6.json");
fs.copyFileSync(MODEL, cfgA6);
const rA6 = install(["--silenzioso", "--cartella", path.join(BENCH, "installed-a6"), "--config", cfgA6, "--weldment-profiles", profilesDir]);
const a6 = json(cfgA6);
const sw6 = a6 && a6.mcpServers && a6.mcpServers.solidworks;
outcome("A6. PROFILI_SALDATURA is written into the entry's env",
  rA6.uscita === 0 && !!sw6 && !!sw6.env && sw6.env.PROFILI_SALDATURA === profilesDir,
  "exit " + rA6.uscita + ", env " + JSON.stringify(sw6 && sw6.env));
outcome("A6. the other servers are untouched",
  !!a6 && equal(a6.mcpServers.filesystem, before.mcpServers.filesystem) && equal(a6.mcpServers.github, before.mcpServers.github), null);
const cfgA6b = path.join(BENCH, "a6b.json");
fs.copyFileSync(MODEL, cfgA6b);
const byteA6b = fs.readFileSync(cfgA6b);
const rA6b = install(["--silenzioso", "--cartella", path.join(BENCH, "installed-a6b"), "--config", cfgA6b, "--weldment-profiles", path.join(BENCH, "no-such-folder")]);
outcome("A6. a weldment profiles folder that does not exist: exit 1, nothing installed, config untouched",
  rA6b.uscita === 1 && !fs.existsSync(path.join(BENCH, "installed-a6b")) && Buffer.compare(byteA6b, fs.readFileSync(cfgA6b)) === 0,
  "exit " + rA6b.uscita);

// ── A5 — uninstall is coherent ───────────────────────────────────────────
console.log("\n=== H19 A5 - uninstall");
const dirA5 = path.join(BENCH, "installed-a5");
const cfgA5 = path.join(BENCH, "a5.json");
fs.copyFileSync(MODEL, cfgA5);
install(["--silenzioso", "--cartella", dirA5, "--config", cfgA5]);
fs.writeFileSync(path.join(dirA5, "domande-senza-risposta.jsonl"), "{\"name\":\"x\"}\n");
const rA5 = install(["--disinstalla", "--cartella", dirA5, "--config", cfgA5]);
const a5 = json(cfgA5);
outcome("A5. uninstall removes the client entry (no --keep-client)",
  rA5.uscita === 0 && !!a5 && !(a5.mcpServers || {}).solidworks,
  "exit " + rA5.uscita + ", servers " + Object.keys((a5 || {}).mcpServers || {}).join(", "));
outcome("A5. ...and leaves the other servers", !!a5 && Object.keys(a5.mcpServers).sort().join(",") === "filesystem,github", null);
outcome("A5. it removes the log the bridge writes beside itself (.jsonl), and then the folder",
  !fs.existsSync(dirA5), fs.existsSync(dirA5) ? "left: " + fs.readdirSync(dirA5).join(", ") : null);

const dirK = path.join(BENCH, "installed-keep");
const cfgK = path.join(BENCH, "keep.json");
fs.copyFileSync(MODEL, cfgK);
install(["--silenzioso", "--cartella", dirK, "--config", cfgK]);
const rK = install(["--disinstalla", "--cartella", dirK, "--config", cfgK, "--keep-client"]);
const k = json(cfgK);
outcome("A5. --keep-client keeps the entry", rK.uscita === 0 && !!k && !!(k.mcpServers || {}).solidworks,
  "exit " + rK.uscita);

const cfgE = path.join(BENCH, "elsewhere.json");
const elsewhereCfg = { mcpServers: { solidworks: { command: "C:\\Somewhere\\Else\\oce-solid-agent-mcp.exe" } } };
fs.writeFileSync(cfgE, JSON.stringify(elsewhereCfg, null, 2));
const rE = install(["--disinstalla", "--cartella", path.join(BENCH, "installed-elsewhere"), "--config", cfgE]);
outcome("A5. an entry that points at another installation is left alone",
  rE.uscita === 0 && equal(json(cfgE), elsewhereCfg), "exit " + rE.uscita + "\n" + rE.testo.slice(-200));

const dirL = path.join(BENCH, "installed-locked");
install(["--silenzioso", "--senza-client", "--cartella", dirL]);
const heldFile = path.join(dirL, "catalog.json");
const lockedReady = path.join(BENCH, "locked.ready");
const holder = require("child_process").spawn("powershell", ["-NoProfile", "-Command",
  "$f=[System.IO.File]::Open('" + heldFile + "','Open','ReadWrite','None'); " +
  "Set-Content -Path '" + lockedReady + "' -Value ok; Start-Sleep -Seconds 60; $f.Close()"], { stdio: "ignore" });
const waitMs = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
for (let i = 0; i < 100 && !fs.existsSync(lockedReady); i++) waitMs(100);
const rL = install(["--disinstalla", "--cartella", dirL, "--keep-client"]);
holder.kill();
waitMs(500);
outcome("A5. a file that cannot be removed: exit 1, and it says which",
  rL.uscita === 1 && rL.testo.includes("could not be removed") && rL.testo.includes("catalog.json"),
  "exit " + rL.uscita + "\n" + (rL.testo.match(/^.*could not be removed.*$/m) || [rL.testo.slice(-200)])[0]);

// ── B5 — Claude Code through its own CLI ─────────────────────────────────
//
// Never against the real Claude Code: a fake `claude.cmd` records what it is
// asked, and the installer's own code is called by reflection from a child
// PowerShell, so no real client configuration is read or written.
console.log("\n=== H19 B5 - claude mcp add");
const fake = path.join(BENCH, "fake-claude");
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
    "$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes('" + INSTALL + "'));" +
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
outcome("B5. the installer finds claude on PATH", c0.out.includes("FOUND=" + path.join(fake, "claude.cmd")),
  c0.out.split(/\r?\n/).slice(0, 2).join(" | "));
const cliLines = c0.args.trim().split(/\r?\n/);
outcome("B5. it removes the old user-scope entry, then adds it with claude mcp add --scope user",
  cliLines.length === 2 && cliLines[0].trim() === "mcp remove --scope user solidworks" &&
  cliLines[1].startsWith("mcp add --scope user solidworks -e \"PROFILI_SALDATURA=C:\\Weld Profiles\" -- \"C:\\Some Folder\\oce-solid-agent-mcp.exe\""),
  c0.args.trim() || c0.out.slice(-200));
outcome("B5. a successful CLI is reported as success", /OK=True/.test(c0.out), c0.out.slice(-120));
const c1 = cliCall(1);
outcome("B5. a failing CLI is reported as failure (the installer then writes the file itself)",
  /OK=False/.test(c1.out), c1.out.slice(-120));

// ═════════════════════════════════════════════════════════════════════════
//  THE H19 REVIEW. Every case below was run red on the installer of bda8946
//  (the L1+L2 commit) before the fixes; the ones that need a profile with no
//  client refuse to install when the installer would look at the real one.
// ═════════════════════════════════════════════════════════════════════════

// Wire, the way the installer calls it for Claude Code, with the fake claude:
// the entry's own variables reach `claude mcp add`, and a backup is made first.
console.log("\n=== H19 review - Claude Code through claude: backup and env kept");
const fakeHome = path.join(BENCH, "fake-claude-home");
fs.mkdirSync(fakeHome);
const fakeClaudeJson = path.join(fakeHome, ".claude.json");
fs.writeFileSync(fakeClaudeJson, JSON.stringify({ mcpServers: { solidworks: {
  command: "C:\\Some Folder\\oce-solid-agent-mcp.exe", env: { RISCONTRO_RADICE: "C:\\Work" } } } }, null, 2));
try { fs.unlinkSync(argsFile); } catch (e) { }
const wireScript =
  "$ErrorActionPreference='Stop';" +
  "$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes('" + INSTALL + "'));" +
  "$bf=[Reflection.BindingFlags]'NonPublic,Public,Static,Instance';" +
  "$ct=$a.GetType('Oce.Solid.Agent.Installer.Client');$c=[Activator]::CreateInstance($ct,$true);" +
  "$ct.GetField('Name',$bf).SetValue($c,'Claude Code');$ct.GetField('File',$bf).SetValue($c,'" + fakeClaudeJson + "');" +
  "$ct.GetField('ClaudeCode',$bf).SetValue($c,$true);" +
  "$tell=[Action[string]]{ param($s) [Console]::WriteLine('TELL '+$s) };" +
  "$p=New-Object object[] 6;$p[0]=$c;$p[1]='C:\\Some Folder\\oce-solid-agent-mcp.exe';$p[2]=$null;$p[3]=$null;$p[4]=$null;$p[5]=$tell;" +
  "$a.GetType('Oce.Solid.Agent.Installer.Program').GetMethod('Wire',$bf).Invoke($null,$p);'WIRED'";
const wr = spawnSync("powershell", ["-NoProfile", "-Command", wireScript],
  { env: Object.assign({}, ENV, { PATH: fake + ";" + ENV.PATH, FAKE_CLAUDE_EXIT: "0" }), encoding: "utf8", timeout: 120000 });
const wireArgs = read(argsFile) || "";
outcome("review. claude mcp add receives the variables already in the entry (RISCONTRO_RADICE)",
  /mcp add --scope user solidworks .*-e "RISCONTRO_RADICE=C:\\Work"/.test(wireArgs),
  wireArgs.trim() || ((wr.stdout || "") + (wr.stderr || "")).slice(-300));
outcome("review. a backup of .claude.json is made before claude rewrites it",
  fs.readdirSync(fakeHome).some(f => /^\.claude\.json\.prima-di-oce-solid-agent-.*\.bak$/.test(f)),
  fs.readdirSync(fakeHome).join(", "));

// A reinstall keeps what is already in the entry's env, by the file route.
console.log("\n=== H19 review - a reinstall keeps the entry's own env");
const keepDir = path.join(BENCH, "installed-keepenv");
const mineProfiles = path.join(BENCH, "my profiles");
fs.mkdirSync(mineProfiles);
const cfgKeep = path.join(BENCH, "keepenv.json");
const keepCfg = { mcpServers: {
  other: { command: "C:\\Other\\server.exe", env: { TOKEN_FILE: "C:\\Other\\t.txt" } },
  solidworks: { command: path.join(keepDir, "oce-solid-agent-mcp.exe"),
                env: { RISCONTRO_RADICE: "C:\\Work", PROFILI_SALDATURA: mineProfiles } } } };
fs.writeFileSync(cfgKeep, JSON.stringify(keepCfg, null, 2));
const rKeep = install(["--silenzioso", "--cartella", keepDir, "--config", cfgKeep]);
const ke = json(cfgKeep);
const ksw = (ke && ke.mcpServers && ke.mcpServers.solidworks) || {};
outcome("review. a reinstall keeps a variable already in the entry (RISCONTRO_RADICE)",
  rKeep.uscita === 0 && !!ksw.env && ksw.env.RISCONTRO_RADICE === "C:\\Work",
  "exit " + rKeep.uscita + ", env " + JSON.stringify(ksw.env));
outcome("review. ...and a PROFILI_SALDATURA chosen by hand, and says it kept it",
  !!ksw.env && ksw.env.PROFILI_SALDATURA === mineProfiles && /kept from the existing client entry/.test(rKeep.testo),
  (rKeep.testo.match(/^.*weldment profiles.*$/m) || ["no weldment line"])[0]);
outcome("review. the other server and its env are untouched",
  !!ke && equal(ke.mcpServers.other, keepCfg.mcpServers.other), null);

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
  const r = install(["--verify", "--json", "--cartella", path.join(BENCH, "nothing-here")], Object.assign({ timeout: 90000 }, opts));
  let j = null;
  try { j = JSON.parse(r.testo.trim().split(/\r?\n/).filter(l => l.startsWith("{")).pop()); } catch (e) { }
  const outside = j ? (j.clients || []).filter(c => !String(c.file || "").toLowerCase().startsWith(dir.toLowerCase())) : null;
  return { ok: !!j && outside.length === 0, detail: j ? outside.map(c => c.name + " -> " + c.file).join("; ") : r.testo.slice(-200) };
}
const emptyProfile = path.join(BENCH, "profile-without-clients");
const emptyOpts = profileEnv(emptyProfile);
const where = looksOnlyIn(emptyProfile, emptyOpts);
outcome("review. in a profile with no client the installer finds none (Claude Code is not 'installed' everywhere)",
  where.ok, where.ok ? null : "it would use: " + where.detail);
if (where.ok) {
  const rNc = install(["--silenzioso", "--cartella", path.join(BENCH, "installed-noclient")], emptyOpts);
  outcome("review. no MCP client: exit 3, it says so and prints the block to paste",
    rNc.uscita === 3 && rNc.testo.includes("no MCP client found") && rNc.testo.includes("Paste this into"),
    "exit " + rNc.uscita + "\n" + rNc.testo.slice(-300));
  outcome("review. ...and it creates no .claude.json nobody reads", !fs.existsSync(path.join(emptyProfile, ".claude.json")),
    fs.readdirSync(emptyProfile).join(", "));

  const ccProfile = path.join(BENCH, "profile-with-claude-code");
  const ccOpts = profileEnv(ccProfile);
  fs.mkdirSync(path.join(ccProfile, ".claude"));
  const ccDir = path.join(BENCH, "installed-cc");
  const rCc = install(["--silenzioso", "--cartella", ccDir], ccOpts);
  const cc = json(path.join(ccProfile, ".claude.json"));
  outcome("review. a profile with a .claude folder: Claude Code is found and wired through its file (no claude on PATH)",
    rCc.uscita === 0 && !!cc && !!cc.mcpServers && !!cc.mcpServers.solidworks &&
    String(cc.mcpServers.solidworks.command).toLowerCase() === path.join(ccDir, "oce-solid-agent-mcp.exe").toLowerCase(),
    "exit " + rCc.uscita + "\n" + rCc.testo.slice(-200));
} else console.log("  (the install cases of this block were NOT run: the installer would touch the real profile)");

// --verify on a profile where one client is wired and the other's config is
// malformed: not all green behind an install that exited 3 for it.
const badProfile = path.join(BENCH, "profile-with-a-bad-config");
const badOpts = profileEnv(badProfile);
fs.mkdirSync(path.join(badProfile, "AppData", "Roaming", "Claude"), { recursive: true });
fs.writeFileSync(path.join(badProfile, "AppData", "Roaming", "Claude", "claude_desktop_config.json"), "{ not json\n");
fs.writeFileSync(path.join(badProfile, ".claude.json"), JSON.stringify({ mcpServers: { solidworks: {
  command: path.join(keepDir, "oce-solid-agent-mcp.exe") } } }, null, 2));
const rvBad = install(["--verify", "--json", "--cartella", keepDir], Object.assign({ timeout: 90000 }, badOpts));
let vBad = null;
try { vBad = JSON.parse(rvBad.testo.trim().split(/\r?\n/).filter(l => l.startsWith("{")).pop()); } catch (e) { }
const clientsCheck = vBad && (vBad.checks || []).find(c => c.name === "clients");
outcome("review. --verify warns about a client config that cannot be read, even when another client is wired",
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
    outcome("review. SOLIDWORKS closed: --prova exits 3", p3.status === 3, "exit " + p3.status);
    const fakeSw = path.join(BENCH, "fake-sldworks");
    fs.mkdirSync(fakeSw);
    const fakeExe = path.join(fakeSw, "SLDWORKS.exe");
    fs.copyFileSync(process.execPath, fakeExe);
    const sw = require("child_process").spawn(fakeExe, ["-e", "setTimeout(()=>{},60000)"], { stdio: "ignore" });
    wait(1500);
    const p6 = spawnSync(bridgeExe, ["--prova"], { env: envAttach, encoding: "utf8", timeout: 120000 });
    sw.kill();
    outcome("review. a SLDWORKS process that does not answer: --prova exits 6 and says why", p6.status === 6 &&
      /administrator/.test(String(p6.stderr || "")), "exit " + p6.status + "\n" + String(p6.stderr || "").split(/\r?\n/).slice(-2).join("\n"));
  }
}

// ── CASE 4 — the config held by another process ─────────────────────────
//
// It is not the whole of case 4 (the real Claude Desktop that holds the file and perhaps
// rewrites it on exit): it is the half that can be done here. A child PowerShell
// opens the file exclusively and holds it; the installer must stop WITHOUT
// having touched it — no `.parziale` left next to it, no new copy —
// and say so with a word that makes clear what to do.
console.log("\n=== CASE 4 (half) - the config locked by another process");
const cfg4 = path.join(BENCH, "bloccato.json");
fs.copyFileSync(MODEL, cfg4);
const byte4 = fs.readFileSync(cfg4);
const signal = path.join(BENCH, "bloccato.pronto");
const keep = require("child_process").spawn("powershell", ["-NoProfile", "-Command",
  "$f=[System.IO.File]::Open('" + cfg4 + "','Open','ReadWrite','None'); " +
  "Set-Content -Path '" + signal + "' -Value ok; Start-Sleep -Seconds 60; $f.Close()"],
  { stdio: "ignore" });
for (let i = 0; i < 100 && !fs.existsSync(signal); i++) wait(100);
if (!fs.existsSync(signal)) {
  outcome("4. the file can be locked for the test", false, "the child PowerShell did not signal");
} else {
  const bakBefore4 = fs.readdirSync(BENCH).filter(f => f.startsWith("bloccato.json.")).length;
  const r4 = install(["--silenzioso", "--cartella", path.join(BENCH, "installato4"), "--config", cfg4]);
  keep.kill();
  wait(500);
  const remains = fs.readdirSync(BENCH).filter(f => f.startsWith("bloccato.json."));
  outcome("4. exit code 3: files installed, the client not wired (H19 B1)", r4.uscita === 3,
    "exit " + r4.uscita);
  outcome("4. it says the file is locked", /locked/i.test(r4.testo),
    (r4.testo.match(/^.*locked.*$/mi) || [r4.testo.slice(-300)])[0].trim());
  outcome("4. the config stayed identical, byte for byte",
    Buffer.compare(byte4, fs.readFileSync(cfg4)) === 0, null);
  outcome("4. no .parziale is left and no copy is born",
    remains.length === bakBefore4, remains.join(", ") || "nothing next to it");
}

// ── CASE 3 — uninstallation ──────────────────────────────────────────────
console.log("\n=== CASE 3 - uninstall");
const extraneous = path.join(folder, "un-mio-log.txt");
fs.writeFileSync(extraneous, "stuff put there by whoever uses the program\n");
// H19 A5: uninstall now removes the client entry too; --config keeps this
// run on the temporary file, away from the real clients of whoever runs it.
const r3 = install(["--disinstalla", "--cartella", folder, "--config", cfg]);
const after3 = json(cfg);
outcome("3. exit 0, and the entry is gone from the client config (H19 A5)",
  r3.uscita === 0 && !!after3 && !(after3.mcpServers || {}).solidworks,
  "exit " + r3.uscita + ", servers " + Object.keys((after3 || {}).mcpServers || {}).join(", "));

const remain = fs.existsSync(folder) ? fs.readdirSync(folder) : null;
outcome("3. the folder is NOT deleted if other people's stuff is left inside",
  remain !== null && remain.includes("un-mio-log.txt"),
  remain === null ? "folder deleted with a file of the user inside"
                   : "resta: " + remain.join(", "));
outcome("3. the bridge is gone",
  remain !== null && !remain.includes("oce-solid-agent-mcp.exe"),
  remain === null ? "n/d" : remain.join(", "));
// H15: installation and uninstallation read the same list. What
// remains must be ONLY the stuff of whoever uses the program.
outcome("3. no file of the installation is left",
  remain !== null && remain.length === 1 && remain[0] === "un-mio-log.txt",
  remain === null ? "n/d" : "resta: " + remain.join(", "));

// ── summary ──────────────────────────────────────────────────────────────
const failed = outcomes.filter(e => !e.passa);
console.log("\n=== SUMMARY");
console.log("  " + (outcomes.length - failed.length) + " PASS, " + failed.length + " FAIL");
console.log("\n  NOT run by this command, and to be done by hand:");
console.log("    1. installation on a clean machine  (VM with SOLIDWORKS, without the bridge)");
console.log("    4. client open during the installation  (the real Claude Desktop: here there is only half");
console.log("       of the file locked by another process; if the client rewrites it on exit, we cannot tell)");
console.log("    5. SOLIDWORKS absent  (machine without SOLIDWORKS)");
if (!ATTACH) {
  console.log("\n  Run without touching SOLIDWORKS (OCE_NO_ATTACH). With the bench stopped, the owner runs");
  console.log("    node Checks\\test-installer.js --attach");
  console.log("  which is the only run where --verify can exit 0. The self-test codes 3 (SOLIDWORKS closed)");
  console.log("  and 4 (not installed or not registered) need SOLIDWORKS closed, or a machine without it.");
}
console.log("\n  bench left in " + BENCH + " if you need to look inside");
process.exit(failed.length ? 1 : 0);
