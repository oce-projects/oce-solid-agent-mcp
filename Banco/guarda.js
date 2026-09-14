// Chiama `guarda_il_pezzo` e stampa poche cose: serve a leggere lo stato fra
// un passo e l'altro del banco, senza mandare un piano.
//
//   node guarda.js                stampa il riassunto
//   node guarda.js tutto          stampa il JSON intero
const { spawn } = require("child_process");
const ESE = process.env.PONTE || "S:/PDM/SolidWorksMcp/bin/SolidWorksMcp.exe";
const tutto = process.argv[2] === "tutto";

const richieste = [
  { jsonrpc: "2.0", id: 1, method: "initialize",
    params: { protocolVersion: "2024-11-05", capabilities: {},
              clientInfo: { name: "banco", version: "1" } } },
  { jsonrpc: "2.0", method: "notifications/initialized" },
  { jsonrpc: "2.0", id: 2, method: "tools/call",
    params: { name: "guarda_il_pezzo", arguments: {} } },
];

const p = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
let avanzo = "";
p.stderr.on("data", () => {});
p.stdout.on("data", d => {
  avanzo += d.toString();
  let i;
  while ((i = avanzo.indexOf("\n")) >= 0) {
    const riga = avanzo.slice(0, i).trim();
    avanzo = avanzo.slice(i + 1);
    if (!riga) continue;
    let m; try { m = JSON.parse(riga); } catch (e) { continue; }
    if (m.id !== 2) continue;
    const t = (m.result && m.result.content || []).map(c => c.text).join("\n");
    if (tutto) { console.log(t); p.stdin.end(); return; }
    let s; try { s = JSON.parse(t); } catch (e) { console.log(t); p.stdin.end(); return; }
    console.log(
      "documento: " + s.titolo +
      "  piani: " + s.piani_disponibili +
      "  feature: " + (s.feature ? s.feature.length : 0) +
      "  altre: " + (s.altre_feature || 0) +
      (s.altre_feature_tipi ? " " + JSON.stringify(s.altre_feature_tipi) : "") +
      "  corpi: " + (s.corpi_totali || 0) +
      "  volume: " + (s.volume_mm3 == null ? "-" : s.volume_mm3));
    p.stdin.end();
  }
});
richieste.forEach(r => p.stdin.write(JSON.stringify(r) + "\n"));
