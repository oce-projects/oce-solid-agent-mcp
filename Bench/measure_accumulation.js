// HOW MUCH A LEFTOVER DOCUMENT LEFT OPEN COSTS.
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
  spawnSync(CLOSE, ["--close"], { encoding: "utf8" });
  const times = [];
  for (let i = 0; i < howMany; i++) {
    spawnSync(NEW, [], { encoding: "utf8" });
    times.push(seconds(() =>
      spawnSync(NODE, [RUN, plane], { encoding: "utf8" })));
    if (closeEachTime) spawnSync(CLOSE, ["--close"], { encoding: "utf8" });
  }
  const remain = spawnSync(CLOSE, [], { encoding: "utf8" }).stdout || "";
  return { times: times, remaining: (remain.split(/\r?\n/)[0] || "").trim() };
}

function tell(docTitle, r) {
  console.log("");
  console.log("=== " + docTitle);
  console.log("  " + r.times.map(t => t.toFixed(1)).join("  "));
  const first = r.times[0];
  const last = r.times[r.times.length - 1];
  console.log("  first " + first.toFixed(1) + " s, last " + last.toFixed(1) +
              " s  (x" + (last / first).toFixed(1) + ")");
  console.log("  at the end: " + r.remaining);
  return { first: first, last: last };
}

console.log("The same plan " + howMany + " times: " + path.basename(plane));

const without = tell("new part each time, NEVER closed", benchRun(false));
const con = tell("new part each time, CLOSED after each plan", benchRun(true));

console.log("");
console.log(without.last > without.first * 1.5 && con.last < con.first * 1.5
  ? "The bench was slowing itself down, and closing no longer does that."
  : "No readable difference between the two ways on this machine.");
