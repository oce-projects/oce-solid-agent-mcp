// HOW MUCH A DISCARDED DOCUMENT LEFT OPEN COSTS.
//
// Entry F1b says that the bench slows itself down: every plan leaves open
// the part it created, and the state enumerates them all at every reading. Here it is
// measured, instead of repeated.
//
// THE SAME PLAN, N TIMES, IN TWO WAYS. It is the only comparison that isolates the
// right variable: different plans have different costs — a gear of 42 teeth
// takes ten times what a round bar does — and by lining up different plans one reads
// the order in which they are written, not the accumulation. With the same plan repeated,
// the only thing that changes between the first time and the last is how many documents
// are on top of it.
//
//   node measure_accumulation.js [count] [plan]
//
// Defaults: 12 runs on cases/bridge/c4s_round.json.
const { spawnSync } = require("child_process");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const NODE = process.execPath;
const RUN = path.join(__dirname, "run_plans.js");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");
const CLOSE = path.join(ROOT, "Checks", "CloseLeftovers", "bin", "CloseLeftovers.exe");

const howMany = parseInt(process.argv[2] || "12", 10);
const plane = process.argv[3] || path.join(__dirname, "cases", "bridge", "c4s_round.json");

function seconds(what) {
  const t0 = process.hrtime.bigint();
  what();
  return Number(process.hrtime.bigint() - t0) / 1e9;
}

function benchRun(closeEachTime) {
  spawnSync(CLOSE, ["--chiudi"], { encoding: "utf8" });
  const times = [];
  for (let i = 0; i < howMany; i++) {
    spawnSync(NEW, [], { encoding: "utf8" });
    times.push(seconds(() =>
      spawnSync(NODE, [RUN, plane], { encoding: "utf8" })));
    if (closeEachTime) spawnSync(CLOSE, ["--chiudi"], { encoding: "utf8" });
  }
  const remain = spawnSync(CLOSE, [], { encoding: "utf8" }).stdout || "";
  return { tempi: times, restano: (remain.split(/\r?\n/)[0] || "").trim() };
}

function tell(docTitle, r) {
  console.log("");
  console.log("=== " + docTitle);
  console.log("  " + r.tempi.map(t => t.toFixed(1)).join("  "));
  const first = r.tempi[0];
  const last = r.tempi[r.tempi.length - 1];
  console.log("  primo " + first.toFixed(1) + " s, ultimo " + last.toFixed(1) +
              " s  (x" + (last / first).toFixed(1) + ")");
  console.log("  alla fine: " + r.restano);
  return { primo: first, ultimo: last };
}

console.log("Lo stesso piano " + howMany + " volte: " + path.basename(plane));

const without = tell("parte nuova ogni volta, MAI chiusa", benchRun(false));
const con = tell("parte nuova ogni volta, CHIUSA dopo ogni piano", benchRun(true));

console.log("");
console.log(without.ultimo > without.first * 1.5 && con.ultimo < con.first * 1.5
  ? "Il banco rallentava se stesso, e chiudendo non lo fa piu'."
  : "Nessuna differenza leggibile fra i due modi su questa macchina.");
