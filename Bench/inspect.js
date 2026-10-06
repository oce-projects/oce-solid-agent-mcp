// Calls `inspect_part` and prints a few things: it serves to read the state between
// one step and the next of the bench, without sending a plan.
//
//   node inspect.js                prints the summary
//   node inspect.js tutto          prints the whole JSON
const { spawn } = require("child_process");
const path = require("path");

// THE PATH IS DERIVED, NOT WRITTEN. Here there used to be the absolute path of
// this development machine: whoever cloned the repository elsewhere and launched the
// command as README.md documents it got an ENOENT on a file that on their
// disk does not exist, with no hint that `OCE_BRIDGE` had to be set.
// `run_plans.js`, two files further on, already did it right.
const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.OCE_BRIDGE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
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
