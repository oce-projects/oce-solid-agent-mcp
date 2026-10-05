// Chiama `inspect_part` e stampa poche cose: serve a leggere lo stato fra
// un passo e l'altro del banco, senza mandare un piano.
//
//   node inspect.js                stampa il riassunto
//   node inspect.js tutto          stampa il JSON intero
const { spawn } = require("child_process");
const path = require("path");

// IL PERCORSO SI RICAVA, NON SI SCRIVE. Qui c'era il percorso assoluto di
// questa macchina di sviluppo: chi clonava il repository altrove e lanciava il
// comando come lo documenta README.md otteneva un ENOENT su un file che sul
// suo disco non esiste, senza nessun indizio che andasse impostata `PONTE`.
// `run_plans.js`, due file piu' in la', lo faceva gia' giusto.
const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const everything = process.argv[2] === "tutto";

const requests = [
  { jsonrpc: "2.0", id: 1, method: "initialize",
    params: { protocolVersion: "2024-11-05", capabilities: {},
              clientInfo: { name: "banco", version: "1" } } },
  { jsonrpc: "2.0", method: "notifications/initialized" },
  { jsonrpc: "2.0", id: 2, method: "tools/call",
    params: { name: "inspect_part", arguments: {} } },
];

const p = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
let leftover = "";
p.stderr.on("data", () => {});
p.stdout.on("data", d => {
  leftover += d.toString();
  let i;
  while ((i = leftover.indexOf("\n")) >= 0) {
    const line = leftover.slice(0, i).trim();
    leftover = leftover.slice(i + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch (e) { continue; }
    if (m.id !== 2) continue;
    const t = (m.result && m.result.content || []).map(c => c.text).join("\n");
    if (everything) { console.log(t); p.stdin.end(); return; }
    let s; try { s = JSON.parse(t); } catch (e) { console.log(t); p.stdin.end(); return; }
    console.log(
      "documento: " + s.document_title +
      "  piani: " + s.planes_available +
      "  feature: " + (s.feature ? s.feature.length : 0) +
      "  altre: " + (s.other_feature || 0) +
      (s.other_feature_kinds ? " " + JSON.stringify(s.other_feature_kinds) : "") +
      "  corpi: " + (s.bodies_total || 0) +
      "  volume: " + (s.volume_mm3 == null ? "-" : s.volume_mm3));
    p.stdin.end();
  }
});
requests.forEach(r => p.stdin.write(JSON.stringify(r) + "\n"));
