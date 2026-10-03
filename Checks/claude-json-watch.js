// How often does a running Claude Code rewrite %USERPROFILE%\.claude.json?
//
//     node Controlli\claude-json-watch.js [seconds] [file]
//
// Entry H19, lot L2 (B5). The installer used to wire Claude Code by reading the
// whole file, adding its entry and writing the whole file back. If a Claude
// Code session is running at the same time (the case "an AI installs the
// bridge"), that session keeps its own copy of the file in memory and may
// write it back later: whatever the installer added in between would be lost,
// with no error anywhere. This script does not prove or disprove that by
// itself; it MEASURES the part that can be measured without a live session
// under test: how often the file changes while Claude Code runs, whether the
// writer leaves temporary files beside it (an atomic write by rename), and
// whether a lock file exists that another writer could honour.
//
// READ ONLY. It never opens the file for writing and never starts Claude Code.
// It reports: number of changes seen, the shortest interval between two
// changes, the temporary and lock files beside the file, and whether the top
// level keys changed (a key that disappears between two samples is the
// symptom of a stale copy written back).
//
// Exit 0 always: this is a measurement, not a check.
const fs = require("fs");
const path = require("path");
const os = require("os");

const seconds = Number(process.argv[2] || 60);
const file = process.argv[3] || path.join(os.homedir(), ".claude.json");
const stepMs = 500;

function sample() {
  try {
    const st = fs.statSync(file);
    let keys = null, servers = null;
    try {
      const j = JSON.parse(fs.readFileSync(file, "utf8"));
      keys = Object.keys(j).sort();
      servers = Object.keys(j.mcpServers || {}).sort();
    } catch (e) { /* caught mid-write or not JSON: counted below */ }
    return { mtime: st.mtimeMs, size: st.size, keys, servers };
  } catch (e) {
    return { missing: true };
  }
}

const sleep = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

if (!fs.existsSync(file)) {
  console.log("no such file: " + file);
  process.exit(0);
}

console.log("watching " + file + " for " + seconds + " s, every " + stepMs + " ms (read only)");
let last = sample();
const changes = [];
let unreadable = 0;
const lostKeys = new Set();
const end = Date.now() + seconds * 1000;
while (Date.now() < end) {
  sleep(stepMs);
  const now = sample();
  if (now.missing) { unreadable++; continue; }
  if (!now.keys) unreadable++;
  if (now.mtime !== last.mtime || now.size !== last.size) {
    changes.push({ at: now.mtime, size: now.size });
    if (last.keys && now.keys)
      for (const k of last.keys) if (!now.keys.includes(k)) lostKeys.add(k);
  }
  if (now.keys) last = now;
}

const gaps = [];
for (let i = 1; i < changes.length; i++) gaps.push(changes[i].at - changes[i - 1].at);
const dir = path.dirname(file), base = path.basename(file);
const beside = fs.readdirSync(dir).filter(n => n !== base && n.startsWith(base));

console.log("changes seen:            " + changes.length);
console.log("shortest gap (ms):       " + (gaps.length ? Math.min(...gaps).toFixed(0) : "n/a"));
console.log("samples not readable:    " + unreadable + "  (caught mid-write, or the file was gone)");
console.log("top level keys lost:     " + (lostKeys.size ? [...lostKeys].join(", ") : "none"));
console.log("mcpServers now:          " + ((last.servers || []).join(", ") || "none"));
console.log("files beside it:         " + (beside.join(", ") || "none"));
console.log("lock file beside it:     " + (beside.some(n => /\.lock$/i.test(n)) ? "yes" : "no"));
process.exit(0);
